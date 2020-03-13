﻿using VSRAD.Syntax.Parser;
using VSRAD.Syntax.Parser.Blocks;
using VSRAD.Syntax.Helpers;
using VSRAD.Syntax.FunctionList.Commands;
using static VSRAD.Syntax.Options.OptionPage;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Task = System.Threading.Tasks.Task;
using Microsoft;

namespace VSRAD.Syntax.FunctionList
{
    public partial class FunctionListControl : UserControl
    {
        private readonly OleMenuCommandService commandService;
        private bool isHideLineNumber = false;
        private SortState FunctionListSortState = SortState.ByName;
        private IList<FunctionBlock> Functions;
        private ListViewItem lastHighlightedItem;

        public FunctionListControl(OleMenuCommandService service)
        {
            var showHideLineNumberCommand = new CommandID(FunctionListCommand.CommandSet, Constants.ShowHideLineNumberCommandId);
            service.AddCommand(new MenuCommand(ShowHideLineNumber, showHideLineNumberCommand));

            this.InitializeComponent();
            this.commandService = service;
            this.Loaded += OnInitializedFunctionList;
        }

        public async Task UpdateFunctionListAsync(IEnumerable<FunctionBlock> newFunctions)
        {
            try
            {
                Functions = newFunctions.ToList();

                var shownFunctions = SearchByNameFilter(newFunctions);

                await AddFunctionsToViewAsync(shownFunctions);
            }
            catch (Exception e)
            {
                Error.LogError(e);
            }
        }

        public async Task HighlightCurrentFunctionAsync(FunctionBlock function)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                if (lastHighlightedItem != null)
                    lastHighlightedItem.IsSelected = false;

                lastHighlightedItem = (ListViewItem)functions.ItemContainerGenerator.ContainerFromItem(function);
                if (lastHighlightedItem != null)
                    lastHighlightedItem.IsSelected = true;
            }
            catch (Exception e)
            {
                Error.LogError(e);
            }
        }

        public void ChangeSortOptions(SortState option)
        {
            try
            {
                FunctionListSortState = option;
                ThreadHelper.JoinableTaskFactory.RunAsync(ReloadFunctionListAsync);
            }
            catch (Exception e)
            {
                Error.LogError(e);
            }
        }

        private Task ReloadFunctionListAsync() => AddFunctionsToViewAsync(Functions);

        private async Task AddFunctionsToViewAsync(IEnumerable<FunctionBlock> functionList)
        {
            switch (FunctionListSortState)
            {
                case SortState.ByLine:
                    functionList = functionList
                        .OrderBy(func => func.FunctionToken.LineNumber);
                    break;

                case SortState.ByName:
                    functionList = functionList
                        .OrderBy(func => func.FunctionToken.TokenName, StringComparer.OrdinalIgnoreCase);
                    break;

                case SortState.ByLineDescending:
                    functionList = functionList
                        .OrderByDescending(func => func.FunctionToken.LineNumber);
                    break;

                case SortState.ByNameDescending:
                    functionList = functionList
                        .OrderByDescending(func => func.FunctionToken.TokenName, StringComparer.OrdinalIgnoreCase);
                    break;
                default:
                    functionList = functionList
                        .OrderBy(func => func.FunctionToken.LineNumber);
                    break;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            functions.Items.Clear();
            foreach (var func in functionList)
                functions.Items.Add(func);
            ResizeFunctionListColumns();
        }

        private void OnInitializedFunctionList(object sender, object args)
            => FunctionListSortState = Package.Instance.OptionPage.SortOptions;

        private void ByNumber_Click(object sender, RoutedEventArgs e)
        {
            switch (FunctionListSortState)
            {
                case SortState.ByLine:
                    FunctionListSortState = SortState.ByLineDescending;
                    break;
                default:
                    FunctionListSortState = SortState.ByLine;
                    break;
            }
            ThreadHelper.JoinableTaskFactory.RunAsync(ReloadFunctionListAsync);
        }

        private void ByName_Click(object sender, RoutedEventArgs e)
        {
            switch (FunctionListSortState)
            {
                case SortState.ByName:
                    FunctionListSortState = SortState.ByNameDescending;
                    break;
                default:
                    FunctionListSortState = SortState.ByName;
                    break;
            }
            ThreadHelper.JoinableTaskFactory.RunAsync(ReloadFunctionListAsync);
        }

        private void FunctionsName_MouseDoubleClick(object sender, MouseButtonEventArgs e) => GoToSelectedItem();

        private void FunctionListWindow_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (null != commandService)
            {
                CommandID menuID = new CommandID(
                    FunctionListCommand.CommandSet,
                    Constants.FunctionListMenu);
                Point p = this.PointToScreen(e.GetPosition(this));
                commandService.ShowContextMenu(menuID, (int)p.X, (int)p.Y);
            }
        }

        private void ShowHideLineNumber(object sender, EventArgs e)
        {
            if (isHideLineNumber)
                isHideLineNumber = false;
            else
                isHideLineNumber = true;

            ResizeFunctionListColumns();
        }

        private IEnumerable<FunctionBlock> SearchByNameFilter(IEnumerable<FunctionBlock> functionList)
        {
            if (functionList == null)
                return Enumerable.Empty<FunctionBlock>();

            return functionList
                .Where(fun => fun.FunctionToken.TokenName.IndexOf(Search.Text, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void Search_TextChanged(object sender, TextChangedEventArgs e)
        {
            var filteredFunctions = SearchByNameFilter(this.Functions);
            ThreadHelper.JoinableTaskFactory.RunAsync(() => AddFunctionsToViewAsync(filteredFunctions));
        }

        private void ResizeFunctionListColumns()
        {
            if (isHideLineNumber)
                this.functionsGridView.Columns[0].Width = 0;
            else
                this.functionsGridView.Columns[0].Width = Double.NaN;

            this.functionsGridView.Columns[1].Width = 0;
            this.functionsGridView.Columns[1].Width = Double.NaN;

            this.functions.UpdateLayout();
            this.LineNumberButtonColumn.Width = new GridLength(this.functionsGridView.Columns[0].ActualWidth);
        }

        public void OnClearSearchField() => this.Search.Text = "";

        public void GoToSelectedItem()
        {
            try
            {
                var function = (FunctionBlock)functions.SelectedItem;
                FunctionList.Instance.GetActiveTextView().ChangeCaretPosition(function.FunctionToken.Line);
            }
            catch (Exception e)
            {
                Error.LogError(e);
            }
        }

        private void FunctionListContentGridOnLoad(object sender, RoutedEventArgs e) => functionListContentGrid.Focus();

        private void FunctionListContentGrid_KeyDown(object sender, KeyEventArgs e) => Keyboard.Focus(Search);

        private void Search_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Up || e.Key == Key.Down)
                Keyboard.Focus(functions);
        }

        private void Functions_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                GoToSelectedItem();
            }
        }
    }
}