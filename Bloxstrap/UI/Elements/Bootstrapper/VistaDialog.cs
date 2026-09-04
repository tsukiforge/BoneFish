using System.Runtime.InteropServices;
using System.Windows.Forms;

using Bloxstrap.UI.Elements.Bootstrapper.Base;

namespace Bloxstrap.UI.Elements.Bootstrapper
{
    // https://youtu.be/h0_AL95Sc3o?t=48

    // a bit hacky, but this is actually a hidden form
    // since taskdialog is part of winforms, it can't really be properly used without a form
    // for example, cross-threaded calls to ui controls can't really be done outside of a form

    public partial class VistaDialog : WinFormsDialogBase
    {
        private TaskDialogPage _dialogPage;

        protected sealed override string _message
        {
            get => _dialogPage.Heading ?? "";
            set => _dialogPage.Heading = value;
        }

        protected sealed override ProgressBarStyle _progressStyle
        {
            set
            {
                if (_dialogPage.ProgressBar is null)
                    return;

                _dialogPage.ProgressBar.State = value switch
                {
                    ProgressBarStyle.Continuous => TaskDialogProgressBarState.Normal,
                    ProgressBarStyle.Blocks => TaskDialogProgressBarState.Normal,
                    ProgressBarStyle.Marquee => TaskDialogProgressBarState.Marquee,
                    _ => _dialogPage.ProgressBar.State
                };
            }
        }

        protected sealed override int _progressMaximum
        {
            get => _dialogPage.ProgressBar?.Maximum ?? 0;
            set
            {
                if (_dialogPage.ProgressBar is null)
                    return;

                _dialogPage.ProgressBar.Maximum = value;
            }
        }

        protected sealed override int _progressValue
        {
            get => _dialogPage.ProgressBar?.Value ?? 0;
            set
            {
                if (_dialogPage.ProgressBar is null)
                    return;

                _dialogPage.ProgressBar.Value = value;
            }
        }

        protected sealed override bool _cancelEnabled
        {
            get => _dialogPage.Buttons[0].Enabled;
            set => _dialogPage.Buttons[0].Enabled = value;
        }

        public VistaDialog()
        {
            InitializeComponent();

            _dialogPage = new TaskDialogPage()
            {
                Icon = new TaskDialogIcon(App.Settings.Prop.BootstrapperIcon.GetIcon()),
                Caption = App.Settings.Prop.BootstrapperTitle,
                RightToLeftLayout = Locale.RightToLeft,

                Buttons = { TaskDialogButton.Cancel },
                ProgressBar = new TaskDialogProgressBar()
                {
                    State = TaskDialogProgressBarState.Marquee
                }
            };

            _message = "Please wait...";
            _cancelEnabled = false;

            _dialogPage.Buttons[0].Click += ButtonCancel_Click;

            SetupDialog();
        }

        public override void ShowSuccess(string message, Action? callback)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(ShowSuccess, message, callback);
            }
            else
            {
                try
                {
                    TaskDialogPage successDialog = new()
                    {
                        Icon = TaskDialogIcon.ShieldSuccessGreenBar,
                        Caption = App.Settings.Prop.BootstrapperTitle,
                        Heading = message,
                        Buttons = { TaskDialogButton.OK }
                    };

                    successDialog.Buttons[0].Click += (_, _) =>
                    {
                        if (callback is not null)
                            callback();

                        App.Terminate();
                    };

                    _dialogPage.Navigate(successDialog);
                    _dialogPage = successDialog;
                }
                catch (COMException ex) when (IsTaskDialogCursorHandleBug(ex))
                {
                    // Same known OS-level TaskDialog bug as in VistaDialog_Load — fall
                    // back to a plain message box instead of crashing the bootstrapper.
                    App.Logger.WriteLine("VistaDialog::ShowSuccess", "TaskDialog navigation failed with 'Invalid cursor handle' COMException, falling back to a plain message box");
                    App.Logger.WriteException("VistaDialog::ShowSuccess", ex);

                    base.ShowSuccess(message, callback);
                }
            }
        }

        public override void CloseBootstrapper()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(CloseBootstrapper);
            }
            else
            {
                _dialogPage.BoundDialog?.Close();
                base.CloseBootstrapper();
            }
        }


        private void VistaDialog_Load(object sender, EventArgs e)
        {
            const string LOG_IDENT = "VistaDialog::Load";

            try
            {
                TaskDialog.ShowDialog(_dialogPage);
            }
            catch (COMException ex) when (IsTaskDialogCursorHandleBug(ex))
            {
                // Known OS-level Windows/.NET bug: COMException "Invalid cursor handle"
                // (0x8007057A). It is transient/sporadic — it also hits unrelated apps
                // (e.g. LaunchBox) and even Windows' own System Restore — not a logic
                // error in BoneFish. Log it, retry once after a short delay, and fall
                // back to a plain WinForms dialog that doesn't use TaskDialog rather
                // than letting .NET show an unhandled exception dialog to the user.
                App.Logger.WriteLine(LOG_IDENT, "TaskDialog failed with 'Invalid cursor handle' COMException, retrying once after a short delay...");
                App.Logger.WriteException(LOG_IDENT, ex);

                Thread.Sleep(500);

                try
                {
                    TaskDialog.ShowDialog(_dialogPage);
                }
                catch (COMException retryEx) when (IsTaskDialogCursorHandleBug(retryEx))
                {
                    App.Logger.WriteLine(LOG_IDENT, "TaskDialog retry failed, falling back to plain WinForms dialog (ProgressDialog)");
                    App.Logger.WriteException(LOG_IDENT, retryEx);

                    ShowFallbackDialog();
                }
            }
        }

        private void ShowFallbackDialog()
        {
            const string LOG_IDENT = "VistaDialog::ShowFallbackDialog";

            // ProgressDialog is a plain WinForms dialog (no Vista TaskDialog), so it is
            // immune to the cursor handle bug. Rewire the bootstrapper to it so progress
            // updates and cancel keep working for the rest of the launch.
            ProgressDialog fallback = new();

            if (App.Bootstrapper is not null)
            {
                App.Bootstrapper.Dialog = fallback;
                fallback.Bootstrapper = App.Bootstrapper;
            }

            // Bootstrapper.Run normally enables cancel on its dialog (always-on since
            // v2.8.0) — make sure the fallback matches that policy in case Run already
            // enabled it on this (abandoned) dialog before the switch.
            fallback.CancelEnabled = true;

            App.Logger.WriteLine(LOG_IDENT, "Showing fallback bootstrapper dialog");

            fallback.ShowBootstrapper();
        }

        private static bool IsTaskDialogCursorHandleBug(COMException ex)
        {
            // 0x8007057A is the HRESULT from the reported crash log; 0x80070579 is
            // ERROR_INVALID_CURSOR_HANDLE. Match on both plus the message text so the
            // catch stays narrow — no other COMException is swallowed.
            return ex.HResult == unchecked((int)0x8007057A)
                || ex.HResult == unchecked((int)0x80070579)
                || ex.Message.Contains("Invalid cursor handle", StringComparison.OrdinalIgnoreCase);
        }
    }
}
