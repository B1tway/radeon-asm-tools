﻿using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using VSRAD.Syntax.Core;
using VSRAD.Syntax.Core.Parser;
using VSRAD.Syntax.Helpers;

namespace VSRAD.Syntax.Options.Instructions
{
    public delegate void InstructionsUpdateDelegate(IInstructionListManager sender, AsmType asmType);
    public interface IInstructionListManager
    {
        IEnumerable<Instruction> GetSelectedSetInstructions(AsmType asmType);
        IEnumerable<Instruction> GetInstructions(AsmType asmType);
        event InstructionsUpdateDelegate InstructionsUpdated;
    }

    public interface IInstructionSetManager
    {
        void ChangeInstructionSet(string selectedSetName);
        IInstructionSet GetInstructionSet();
        IReadOnlyList<IInstructionSet> GetInstructionSets();
        event AsmTypeChange AsmTypeChanged;
    }

    public delegate void AsmTypeChange();

    [Export(typeof(IInstructionListManager))]
    [Export(typeof(IInstructionSetManager))]
    internal sealed class InstructionListManager : IInstructionListManager, IInstructionSetManager
    {
        public static IInstructionSetManager Instance;

        private readonly List<IInstructionSet> _radAsm1InstructionSets;
        private readonly List<IInstructionSet> _radAsm2InstructionSets;
        private readonly List<Instruction> _radAsm1Instructions;
        private readonly List<Instruction> _radAsm2Instructions;

        private AsmType _activeDocumentType;
        private IInstructionSet _radAsm1SelectedSet;
        private IInstructionSet _radAsm2SelectedSet;

        public event InstructionsUpdateDelegate InstructionsUpdated;
        public event AsmTypeChange AsmTypeChanged;

        [ImportingConstructor]
        public InstructionListManager(IInstructionListLoader instructionListLoader, IDocumentFactory documentFactory)
        {
            instructionListLoader.InstructionsUpdated += InstructionsLoaded;
            documentFactory.ActiveDocumentChanged += ActiveDocumentChanged;
            documentFactory.DocumentCreated += ActiveDocumentChanged;

            _radAsm1InstructionSets = new List<IInstructionSet>();
            _radAsm2InstructionSets = new List<IInstructionSet>();
            _radAsm1Instructions = new List<Instruction>();
            _radAsm2Instructions = new List<Instruction>();
            _activeDocumentType = AsmType.Unknown;
            InstructionsLoaded(instructionListLoader.InstructionSets);
            Instance = this;
        }

        private void InstructionsLoaded(IEnumerable<IInstructionSet> instructions)
        {
            _radAsm1InstructionSets.Clear();
            _radAsm2InstructionSets.Clear();
            _radAsm1Instructions.Clear();
            _radAsm2Instructions.Clear();

            foreach (var typeGroup in instructions.GroupBy(s => s.Type))
            {
                switch (typeGroup.Key)
                {
                    case InstructionType.RadAsm1: _radAsm1InstructionSets.AddRange(typeGroup.AsEnumerable()); break;
                    case InstructionType.RadAsm2: _radAsm2InstructionSets.AddRange(typeGroup.AsEnumerable()); break;
                }
            }

            _radAsm1Instructions.AddRange(_radAsm1InstructionSets.SelectMany(s => s.Select(i => i)));
            _radAsm2Instructions.AddRange(_radAsm2InstructionSets.SelectMany(s => s.Select(i => i)));
            _radAsm1SelectedSet = null;
            _radAsm2SelectedSet = null;

            AsmTypeChanged?.Invoke();
            InstructionsUpdatedInvoke(AsmType.RadAsmCode);
        }

        public IEnumerable<Instruction> GetSelectedSetInstructions(AsmType asmType)
        {
            switch (asmType)
            {
                case AsmType.RadAsm: return _radAsm1SelectedSet ?? (IEnumerable<Instruction>)_radAsm1Instructions;
                case AsmType.RadAsm2: return _radAsm2SelectedSet ?? (IEnumerable<Instruction>)_radAsm2Instructions;
                default: return Enumerable.Empty<Instruction>();
            }
        }

        public IEnumerable<Instruction> GetInstructions(AsmType asmType)
        {
            switch (asmType)
            {
                case AsmType.RadAsm: return _radAsm1Instructions;
                case AsmType.RadAsm2: return _radAsm2Instructions;
                default: return Enumerable.Empty<Instruction>();
            }
        }

        private void ActiveDocumentChanged(IDocument activeDocument)
        {
            var newActiveDocumentAsm = activeDocument?.CurrentSnapshot.GetAsmType() ?? AsmType.Unknown;
            if (newActiveDocumentAsm == _activeDocumentType) return;

            _activeDocumentType = newActiveDocumentAsm;
            AsmTypeChanged?.Invoke();
        }

        public void ChangeInstructionSet(string selected)
        {
            if (selected == null)
            {
                switch (_activeDocumentType)
                {
                    case AsmType.RadAsm: _radAsm1SelectedSet = null; break;
                    case AsmType.RadAsm2: _radAsm2SelectedSet = null; break;
                }
            }
            else
            {
                switch (_activeDocumentType)
                {
                    case AsmType.RadAsm: ChangeInstructionSet(selected, _radAsm1InstructionSets, ref _radAsm1SelectedSet);  break;
                    case AsmType.RadAsm2: ChangeInstructionSet(selected, _radAsm2InstructionSets, ref _radAsm2SelectedSet); break;
                }
            }

            InstructionsUpdatedInvoke(_activeDocumentType);
        }

        private void ChangeInstructionSet(string setName, List<IInstructionSet> sets, ref IInstructionSet selectedSet)
        {
            var set = sets.Find(s => s.SetName == setName);
            if (set == null)
            {
                Error.ShowErrorMessage($"Cannot find selected instruction set: {setName}", "Instruction set selector");
                selectedSet = null;
                return;
            }

            selectedSet = set;
        }

        private void InstructionsUpdatedInvoke(AsmType type)
        {
            Asm1Parser.UpdateInstructions(this, type);
            Asm2Parser.UpdateInstructions(this, type);
            InstructionsUpdated?.Invoke(this, type);
        }

        public IReadOnlyList<IInstructionSet> GetInstructionSets() =>
            _activeDocumentType == AsmType.RadAsm
                ? _radAsm1InstructionSets
                : _activeDocumentType == AsmType.RadAsm2
                    ? _radAsm2InstructionSets : new List<IInstructionSet>();

        public IInstructionSet GetInstructionSet()
        {
            switch (_activeDocumentType)
            {
                case AsmType.RadAsm: return _radAsm1SelectedSet;
                case AsmType.RadAsm2: return _radAsm2SelectedSet;
                default: return null;
            }
        }
    }
}
