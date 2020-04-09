﻿using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using VSRAD.Syntax.Helpers;
using VSRAD.Syntax.Parser;
using DisplayNameAttribute = System.ComponentModel.DisplayNameAttribute;
using Task = System.Threading.Tasks.Task;

namespace VSRAD.Syntax.Options
{
    public class OptionPage : DialogPage
    {
        const string asm1CollectionPath = "Asm1CollectionFileExtensions";
        const string asm2CollectionPath = "Asm2CollectionFileExtensions";
        private static readonly Regex fileExtensionRegular = new Regex(@"^\.\w+$");
        private readonly IContentTypeRegistryService _contentTypeRegistryService;
        private readonly IFileExtensionRegistryService _fileExtensionRegistryService;
        private readonly TextViewObserver _textViewObserver;
        private readonly IContentType _asm1ContentType;
        private readonly IContentType _asm2ContentType;
        private readonly CollectionConverter _converter;

        public OptionPage()
        {
            _contentTypeRegistryService = Package.Instance.GetMEFComponent<IContentTypeRegistryService>();
            _fileExtensionRegistryService = Package.Instance.GetMEFComponent<IFileExtensionRegistryService>();
            _textViewObserver = Package.Instance.GetMEFComponent<TextViewObserver>();

            _asm1ContentType = _contentTypeRegistryService.GetContentType(Constants.RadeonAsmSyntaxContentType);
            _asm2ContentType = _contentTypeRegistryService.GetContentType(Constants.RadeonAsm2SyntaxContentType);
            _converter = new CollectionConverter();
        }

        [Category("Function list")]
        [DisplayName("Function list default sort option")]
        [Description("Set default sort option for Function List")]
        [DefaultValue(SortState.ByName)]
        public SortState SortOptions { get; set; } = SortState.ByName;

        [Category("Syntax highlight")]
        [DisplayName("Indent guide lines")]
        [Description("Enable/disable indent guide lines")]
        [DefaultValue(true)]
        public bool IsEnabledIndentGuides { get; set; } = true;

        [Category("Syntax asm1 file extensions")]
        [DisplayName("Asm1 file extensions")]
        [Description("List of file extensions for the asm1 syntax")]
        [Editor(@"System.Windows.Forms.Design.StringCollectionEditor, System.Design, Version=2.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a", typeof(System.Drawing.Design.UITypeEditor))]
        public List<string> Asm1FileExtensions { get; set; }

        [Category("Syntax asm2 file extensions")]
        [DisplayName("Asm2 file extensions")]
        [Description("List of file extensions for the asm2 syntax")]
        [Editor(@"System.Windows.Forms.Design.StringCollectionEditor, System.Design, Version=2.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a", typeof(System.Drawing.Design.UITypeEditor))]
        public List<string> Asm2FileExtensions { get; set; }

        public enum SortState
        {
            [Description("by line number")]
            ByLine = 1,
            [Description("by line number descending")]
            ByLineDescending = 2,
            [Description("by name")]
            ByName = 3,
            [Description("by name descending")]
            ByNameDescending = 4,
        }

        protected override void OnApply(PageApplyEventArgs e)
        {
            try
            {
                base.OnApply(e);

                ValidateExtensions(Asm1FileExtensions);
                ValidateExtensions(Asm2FileExtensions);
                FunctionList.FunctionList.TryUpdateSortOptions(SortOptions);
                ThreadHelper.JoinableTaskFactory.RunAsync(() => ChangeExtensionsAndUpdateConentTypesAsync());
            }
            catch(Exception ex)
            {
                Error.ShowWarning(ex);
            }
        }
        
        private void ValidateExtensions(List<string> extensions)
        {
            var sb = new StringBuilder();
            foreach (var ext in extensions)
            {
                if (!fileExtensionRegular.IsMatch(ext))
                    sb.AppendLine($"Invalid file extension format \"{ext}\"");
            }
            if (sb.Length != 0)
            {
                sb.AppendLine();
                sb.AppendLine("Format example: .asm");
                throw new ArgumentException(sb.ToString());
            }
        }

        public override void SaveSettingsToStorage()
        {
            base.SaveSettingsToStorage();

            var settingsManager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
            var userSettingsStore = settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);

            SaveCollectionSettings(userSettingsStore, asm1CollectionPath, Asm1FileExtensions, nameof(Asm1FileExtensions));
            SaveCollectionSettings(userSettingsStore, asm2CollectionPath, Asm2FileExtensions, nameof(Asm2FileExtensions));
        }

        public override void LoadSettingsFromStorage()
        {
            base.LoadSettingsFromStorage();

            var settingsManager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
            var userSettingsStore = settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);

            var notExist = false;
            if (!userSettingsStore.PropertyExists(asm1CollectionPath, nameof(Asm1FileExtensions)))
            {
                Asm1FileExtensions = Constants.DefaultFileExtensionAsm1;
                notExist = true;
            }
            if (!userSettingsStore.PropertyExists(asm2CollectionPath, nameof(Asm2FileExtensions)))
            {
                Asm2FileExtensions = Constants.DefaultFileExtensionAsm2;
                notExist = true;
            }
            if (notExist) return;

            var converter = new CollectionConverter();
            Asm1FileExtensions = converter.ConvertFrom(
                userSettingsStore.GetString(asm1CollectionPath, nameof(Asm1FileExtensions))) as List<string>;
            Asm2FileExtensions = converter.ConvertFrom(
                userSettingsStore.GetString(asm2CollectionPath, nameof(Asm2FileExtensions))) as List<string>;
        }

        public void ChangeExtensions()
        {
            try
            {
                ChangeExtensions(_asm1ContentType, Asm1FileExtensions);
                ChangeExtensions(_asm2ContentType, Asm2FileExtensions);
            }
            catch (InvalidOperationException e)
            {
                Error.ShowWarning(e);
            }
        }

        public async Task ChangeExtensionsAndUpdateConentTypesAsync()
        {
            try
            {
                ChangeExtensions();
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                UpdateOpenedTextViewContentType();
            }
            catch (Exception e)
            {
                Error.LogError(e);
            }
        }

        private void UpdateOpenedTextViewContentType()
        {
            foreach (var textView in _textViewObserver.TextViews)
            {
                UpdateTextViewContentType(textView);
            }
        }

        private void UpdateTextViewContentType(IWpfTextView wpfTextView)
        {
            var path = wpfTextView.GetPath();
            if (path != null)
            {
                var extension = System.IO.Path.GetExtension(path);
                UpdateTextBufferContentType(wpfTextView.TextBuffer, extension);
            }
        }

        private void UpdateTextBufferContentType(ITextBuffer textBuffer, string fileExtension)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (textBuffer == null)
                return;

            if (Asm1FileExtensions.Contains(fileExtension))
                UpdateTextBufferContentType(textBuffer, _asm1ContentType);

            if (Asm2FileExtensions.Contains(fileExtension))
                UpdateTextBufferContentType(textBuffer, _asm2ContentType);
        }

        private void UpdateTextBufferContentType(ITextBuffer textBuffer, IContentType contentType)
        {
            if (textBuffer == null || contentType == null)
                return;

            textBuffer.ChangeContentType(contentType, null);
            var parserManager = textBuffer.Properties.GetOrCreateSingletonProperty(() => new ParserManger());

            if (contentType == _asm1ContentType)
                parserManager.InitializeAsm1(textBuffer);

            if (contentType == _asm2ContentType)
                parserManager.InitializeAsm2(textBuffer);
        }

        private void SaveCollectionSettings(WritableSettingsStore userSettingsStore, string collectionPath, List<string> collection, string propertyName)
        {
            if (!userSettingsStore.CollectionExists(collectionPath))
                userSettingsStore.CreateCollection(collectionPath);

            userSettingsStore.SetString(
                collectionPath,
                propertyName,
                _converter.ConvertTo(collection, typeof(string)) as string);
        }

        private void ChangeExtensions(IContentType contentType, IEnumerable<string> extensions)
        {
            foreach (var ext in _fileExtensionRegistryService.GetExtensionsForContentType(contentType))
                _fileExtensionRegistryService.RemoveFileExtension(ext);

            foreach (var ext in extensions)
                _fileExtensionRegistryService.AddFileExtension(ext, contentType);
        }

        private class CollectionConverter : TypeConverter
        {
            private const string delimiter = "#!!#";

            public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            {
                return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
            }

            public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
            {
                return destinationType == typeof(IList<string>) || base.CanConvertTo(context, destinationType);
            }

            public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
            {
                return !(value is string v)
                    ? base.ConvertFrom(context, culture, value)
                    : v.Split(new[] { delimiter }, StringSplitOptions.RemoveEmptyEntries).ToList();
            }

            public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
            {
                var v = value as IList<string>;
                if (destinationType != typeof(string) || v == null)
                {
                    return base.ConvertTo(context, culture, value, destinationType);
                }
                return string.Join(delimiter, v);
            }
        }
    }
}