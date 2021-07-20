﻿using System;
using System.ComponentModel;
using VSRAD.Syntax.Helpers;

namespace VSRAD.Syntax.Options
{
    public class GeneralOptionPage : BaseOptionPage<GeneralOptions>
    {
        private readonly OptionsProvider _optionsEventProvider;

        public GeneralOptionPage() : base()
        {
            _optionsEventProvider = Package.Instance.GetMEFComponent<OptionsProvider>();
        }

        protected override void OnApply(PageApplyEventArgs e)
        {
            base.OnApply(e);

            try
            {
                _optionsEventProvider.OptionsUpdatedInvoke();
            }
            catch (Exception ex)
            {
                Error.ShowWarning(ex);
            }
        }
    }
}