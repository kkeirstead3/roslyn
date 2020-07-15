using System;
using System.Collections.Generic;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Roslyn.Utilities;
using Microsoft.CodeAnalysis.EditAndContinue;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.Clr;
using Microsoft.VisualStudio.Debugger.ComponentInterfaces;
using Microsoft.VisualStudio.Debugger.Breakpoints;
using Microsoft.VisualStudio.Debugger.Symbols;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.IO;
//using VSDebugEngine.ManagedEnc.Remap;
using System.Globalization;
using System.Diagnostics;


using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.Editor.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Internal.Log;
using Microsoft.CodeAnalysis.Notification;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Roslyn.Utilities;


namespace Microsoft.CodeAnalysis.Features
{
    internal sealed class ConditionBreakpointResolver : IDkmClrBreakpointConditionRequestResolver
    {
        public struct ActiveStatementUpdate
        {
            public Guid m_ThreadId { get; set; }
            public DkmClrMethodId m_MethodId { get; set; }
            public int m_ILOffset { get; set; }
            public DkmTextSpan m_NewSpan { get; set; }
        }

        public struct SourceLineUpdate
        {
            public int OldLine { get; set; }
            public int NewLine { get; set; }
        }

        public struct SequencePointsUpdate
        {
            public String FileName { get; set; }
            public ReadOnlyCollection<SourceLineUpdate> LineUpdates { get; set; }
        }

        public struct ManagedModuleUpdate
        {
            public Guid ModuleId { get; set; }

            public ReadOnlyCollection<byte> ILDelta { get; set; }
            public ReadOnlyCollection<byte> MetadataDelta { get; set; }
            public ReadOnlyCollection<byte> PdbDelta { get; set; }
            public ReadOnlyCollection<SequencePointsUpdate> SequencePoints { get; set; }
            public ReadOnlyCollection<int> UpdatedMethods { get; set; }
            public ReadOnlyCollection<ActiveStatementUpdate> ActiveStatements { get; set; }
            public ReadOnlyCollection<DkmExceptionRegionUpdate> ExceptionRegions { get; set; }
        }
        DkmManagedModuleUpdate IDkmClrBreakpointConditionRequestResolver.GetUpdate(DkmClrBreakpointConditionRequest clrBreakpointConditionRequest, DkmThread Thread, DkmSourcePosition UpdatePosition)
        {
            // Temporarily using full path (Chuck says this is OK since this is only temporary and for me)
            string json = File.ReadAllText("D:/Sources/Concord/Concord/src/ManImpl45/ManagedEnc/MockData/SimpleDeltas.json");

            string currPath = clrBreakpointConditionRequest.TargetSourcePosition.DocumentName;

            string fullCSFile = File.ReadAllText(currPath);

            string appendedCSFile = AppendProgram(fullCSFile, UpdatePosition, clrBreakpointConditionRequest);

            // This should all happen instantaneously so that the user never sees this on-screen
            File.WriteAllText(currPath, appendedCSFile);

            /*
            IComponentModel componentModel = ComponentManager.GetComponentModel();
            s_providers = componentModel.DefaultExportProvider.GetExports<IEditAndContinueManagedModuleUpdateProvider, UIContextMetadata>();


            try
            {
                // request the module updates from the providers
                Task.Run(async () =>
                {
                    foreach (var provider in providers)
                    {
                        var updates = await provider.Value.GetManagedModuleUpdatesAsync(null);
                    }
                });
            }
            catch (OperationCanceledException)
            {

            }
            */
            return getDeltas(json, Thread.UniqueId);
        }

        DkmManagedModuleUpdate IDkmClrBreakpointConditionRequestResolver.Undo(DkmClrEditAndContinueConditionalBreakpoint ClrEnCConditionalBreakpoint, DkmThread Thread)
        {
            // Temporarily using full path (Chuck says this is OK since this is only temporary and for me)
            string json = File.ReadAllText("D:/Sources/Concord/Concord/src/ManImpl45/ManagedEnc/MockData/SimpleDeltasReversed.json");

            return getDeltas(json, Thread.UniqueId);
        }

        public static DkmManagedModuleUpdate getDeltas(string json, Guid threadID)
        {
            DkmModule[] modules = DkmModule.GetModules(); // For our purposes, only one module, so access 0th element

            DkmModule currModule = modules[0];
            IEnumerable<DkmClrModuleInstance> clrModuleInstances = currModule.GetModuleInstances().OfType<DkmClrModuleInstance>();

            DkmClrModuleInstance currModuleInstance = clrModuleInstances.First();

            Newtonsoft.Json.Linq.JObject jsonObject = Newtonsoft.Json.Linq.JObject.Parse(json);

            ReadOnlyCollection<DkmActiveStatementUpdate> allASUs = getAllASUs(jsonObject, threadID);

            ReadOnlyCollection<DkmSequencePointsUpdate> allSPs = getAllSPs(jsonObject, threadID);

            ManagedModuleUpdate tempDeltas = JsonConvert.DeserializeObject<ManagedModuleUpdate>(json);
            DkmManagedModuleUpdate deltas = DkmManagedModuleUpdate.Create(
                currModuleInstance.Mvid,
                tempDeltas.ILDelta,
                tempDeltas.MetadataDelta,
                tempDeltas.PdbDelta,
                allSPs,
                tempDeltas.UpdatedMethods,
                allASUs,
                tempDeltas.ExceptionRegions);

            return deltas;
        }

        public static ReadOnlyCollection<DkmActiveStatementUpdate> getAllASUs(Newtonsoft.Json.Linq.JObject jsonFromFile, Guid threadID)
        {
            int activeStatementCount = jsonFromFile["ActiveStatements"].Count();

            List<DkmActiveStatementUpdate> ASUs = new List<DkmActiveStatementUpdate>();

            for (int index = 0; index < activeStatementCount; ++index)
            {
                var currASSerialized = jsonFromFile["ActiveStatements"][index];

                DkmClrMethodId parsed_methodID = JsonConvert.DeserializeObject<DkmClrMethodId>(currASSerialized["MethodId"].ToString());
                int parsed_ILOffset = JsonConvert.DeserializeObject<int>(currASSerialized["ILOffset"].ToString());
                DkmTextSpan parsed_NewSpan = JsonConvert.DeserializeObject<DkmTextSpan>(currASSerialized["NewSpan"].ToString());

                DkmActiveStatementUpdate convertedASU = DkmActiveStatementUpdate.Create(threadID, parsed_methodID, parsed_ILOffset, parsed_NewSpan);

                ASUs.Add(convertedASU);
            }

            ReadOnlyCollection<DkmActiveStatementUpdate> readOnlyASUs = new ReadOnlyCollection<DkmActiveStatementUpdate>(ASUs);

            return readOnlyASUs;
        }

        public static ReadOnlyCollection<DkmSequencePointsUpdate> getAllSPs(Newtonsoft.Json.Linq.JObject jsonFromFile, Guid threadID)
        {
            int sequencePointCount = jsonFromFile["SequencePoints"].Count();

            List<DkmSequencePointsUpdate> SPs = new List<DkmSequencePointsUpdate>();

            for (int index = 0; index < sequencePointCount; ++index)
            {
                var currSPSerialized = jsonFromFile["SequencePoints"][index];

                string parsed_filename = currSPSerialized["FileName"].ToString();

                List<DkmSourceLineUpdate> sourceLineUpdates = new List<DkmSourceLineUpdate>();

                for (int counter = 0; counter < currSPSerialized["LineUpdates"].Count(); ++counter)
                {
                    int oldLine = int.Parse(currSPSerialized["LineUpdates"][counter]["OldLine"].ToString(), CultureInfo.CurrentCulture);
                    int newLine = int.Parse(currSPSerialized["LineUpdates"][counter]["NewLine"].ToString(), CultureInfo.CurrentCulture);
                    sourceLineUpdates.Add(DkmSourceLineUpdate.Create(oldLine, newLine));
                }

                var parsed_sourceLineUpdates = new ReadOnlyCollection<DkmSourceLineUpdate>(sourceLineUpdates);

                DkmSequencePointsUpdate convertedSP = DkmSequencePointsUpdate.Create(parsed_filename, parsed_sourceLineUpdates);

                SPs.Add(convertedSP);
            }

            ReadOnlyCollection<DkmSequencePointsUpdate> readOnlySPs = new ReadOnlyCollection<DkmSequencePointsUpdate>(SPs);

            return readOnlySPs;
        }

        public static string AppendProgram(string programText, DkmSourcePosition UpdatePosition, DkmClrBreakpointConditionRequest clrBreakpointConditionRequest)
        {
            string beforeBPProgramText = "";
            string afterBPProgramText = programText;

            int currLineNumber = 1;
            int breakpointLine = UpdatePosition.TextSpan.StartLine;

            while (currLineNumber < breakpointLine)
            {
                int newlineIndex = afterBPProgramText.IndexOf('\n');
                beforeBPProgramText += afterBPProgramText.Substring(0, newlineIndex + 1);
                afterBPProgramText = afterBPProgramText.Substring(newlineIndex + 1);
                ++currLineNumber;
            }

            bool hittingSpaces = true;

            int characterIndex = 0;
            string spaceString = "";

            while (hittingSpaces)
            {
                if (afterBPProgramText[characterIndex] != ' ')
                {
                    hittingSpaces = false;
                }
                else
                {
                    ++characterIndex;
                    spaceString += ' ';
                }
            }

            //string addedText = "";

            string addedText = "#line hidden\n" + "if (" + clrBreakpointConditionRequest.Condition.Text + ")\n" + "#line " + currLineNumber.ToString(CultureInfo.CurrentCulture) + "\n" + spaceString + "System.Diagnostics.Debugger.Break();\n" + "#line " + currLineNumber.ToString(CultureInfo.CurrentCulture) + "\n";

            return beforeBPProgramText + addedText + afterBPProgramText;
        }
    }
}

