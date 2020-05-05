﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace VSRAD.Package.DebugVisualizer.MouseMove
{
    sealed class ReorderOperation : IMouseMoveOperation
    {
        private readonly VisualizerTable _table;

        private bool _operationStarted;
        private int _hoverRowIndex;
        private List<DataGridViewRow> _selectedRows;
        private List<DataGridViewRow> _rowsToMove;

        public ReorderOperation(VisualizerTable table)
        {
            _table = table;
        }

        public bool AppliesOnMouseDown(MouseEventArgs e, DataGridView.HitTestInfo hit)
        {
            if (hit.Type != DataGridViewHitTestType.RowHeader
                || hit.RowIndex <= VisualizerTable.SystemRowIndex
                || hit.RowIndex == _table.NewWatchRowIndex)
                return false;

            _operationStarted = false;
            _hoverRowIndex = hit.RowIndex;
            return true;
        }

        public bool HandleMouseMove(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return false;

            if (!_operationStarted)
            {
                _selectedRows = _table.SelectedRows.Cast<DataGridViewRow>().Where(r => r.Index != _table.NewWatchRowIndex).ToList();
                if (_selectedRows.Contains(_table.Rows[_hoverRowIndex]))
                    _rowsToMove = _selectedRows.Where(r => r.Index != VisualizerTable.SystemRowIndex).ToList();
                else
                    _rowsToMove = new List<DataGridViewRow>() { _table.Rows[_hoverRowIndex] };
            }

            var nomalizedMouseX = Math.Min(Math.Max(e.X, 1), _table.Width - 2);
            var hit = _table.HitTest(nomalizedMouseX, e.Y);
            var indexDiff = hit.RowIndex - _hoverRowIndex;

            if (indexDiff != 0
                && _rowsToMove.Max(r => r.Index) + indexDiff < _table.NewWatchRowIndex
                && _rowsToMove.Min(r => r.Index) + indexDiff > VisualizerTable.SystemRowIndex)
            {
                _operationStarted = true;

                _rowsToMove.Sort((r1, r2) => (indexDiff < 0) ? r1.Index.CompareTo(r2.Index) : r2.Index.CompareTo(r1.Index));

                foreach (var row in _rowsToMove)
                {
                    var oldIndex = row.Index;
                    _table.Rows.Remove(row);
                    _table.Rows.Insert(oldIndex + indexDiff, row);
                }

                // DataGridView appears to reset selection when adding/removing rows
                // (is there a less hacky way to preserve it?)
                foreach (var row in _selectedRows)
                    row.Selected = true;

                _hoverRowIndex = hit.RowIndex;
            }

            return true;
        }

        public bool OperationStarted() => _operationStarted;
    }
}
