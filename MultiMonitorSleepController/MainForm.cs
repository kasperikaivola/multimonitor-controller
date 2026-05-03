using System.ComponentModel;
using System.Drawing;
using System.IO.Pipes;
using System.Windows.Forms;

namespace MultiMonitorSleepController;

public sealed class MainForm : Form
{
    private readonly MonitorControllerService _monitorController = new();

    private readonly DisplayOutputControllerService _displayOutputController = new();

    private readonly OverlayControllerService _overlayController = new();

    private readonly SettingsStore _settingsStore = new();

    private readonly BindingList<MonitorRow> _monitorRows = new();

    private readonly Dictionary<int, string> _registeredHotkeys = new();

    private readonly DataGridView _monitorGrid = new();

    private readonly ListBox _profileList = new();

    private readonly TextBox _profileNameTextBox = new();

    private readonly ComboBox _modifierComboBox = new();

    private readonly ComboBox _keyComboBox = new();

    private readonly TextBox _logTextBox = new();

    private readonly Label _statusLabel = new();

    private readonly CheckBox _darkModeCheckBox = new();

    private readonly TrackBar _brightnessTrackBar = new();

    private readonly Label _brightnessLabel = new();

    private readonly System.Windows.Forms.Timer _brightnessSyncTimer = new();

    private BrightnessTrayIcon? _brightnessTrayIcon;

    private GlobalKeyboardHook? _globalKeyboardHook;

    private bool _emergencyWakeArmed;

    private DateTime _emergencyWakeArmedAtUtc = DateTime.MinValue;

    private static readonly TimeSpan EmergencyWakeActivationDelay = TimeSpan.FromMilliseconds(1000);

    private AppSettings _settings = new();

    private int _nextHotkeyId = 1;

    public MainForm()
    {
        _settings = _settingsStore.Load();

        Text = "Multi-Monitor Sleep Controller";
        Width = 1260;
        Height = 800;
        MinimumSize = new Size(980, 640);
        StartPosition = FormStartPosition.CenterScreen;

        BuildUi();
        PopulateHotkeyEditors();
        StartVirtualDriverIpcClient();

        _darkModeCheckBox.Checked = _settings.DarkMode;
        ApplyTheme(_settings.DarkMode);

        RefreshMonitors();
        ReloadProfileList();

        _brightnessSyncTimer.Interval = 5000;
        _brightnessSyncTimer.Tick += (_, _) =>
        {
            RefreshMonitors();
            SyncBrightnessFromMonitors();
        };
        SyncBrightnessFromMonitors();
        _brightnessSyncTimer.Start();

        _brightnessTrayIcon = new BrightnessTrayIcon(
            onBrightnessChanged: value =>
            {
                _brightnessTrackBar.Value = value;
                _brightnessLabel.Text = $"Global Brightness: {value}%";
                ApplyGlobalBrightness();
            },
            onShowMainWindow: () =>
            {
                Show();
                WindowState = FormWindowState.Normal;
                Activate();
            });
        _brightnessTrayIcon.UpdateBrightness(_brightnessTrackBar.Value);

        Log("Application started. Use mode Auto/DDC/Blackout per monitor for best stability.");
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RegisterProfileHotkeys();
        EnsureGlobalKeyboardHook();
        ApplyWindowChromeTheme(_settings.DarkMode);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterProfileHotkeys();
        DisposeGlobalKeyboardHook();
        base.OnHandleDestroyed(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            
            // Save settings when hiding
            SaveMonitorModesFromGrid(false);
            _settings.DarkMode = _darkModeCheckBox.Checked;
            _settingsStore.Save(_settings);
            return;
        }

        UnregisterProfileHotkeys();
        DisposeGlobalKeyboardHook();
        SaveMonitorModesFromGrid(false);
        _settings.DarkMode = _darkModeCheckBox.Checked;
        _settingsStore.Save(_settings);

        _brightnessTrayIcon?.Dispose();
        _overlayController.Dispose();
        _monitorController.Dispose();

        base.OnFormClosing(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY)
        {
            var hotkeyId = m.WParam.ToInt32();

            if (_registeredHotkeys.TryGetValue(hotkeyId, out var profileName))
            {
                var profile = _settings.Profiles.FirstOrDefault(
                    p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase));

                if (profile is not null)
                {
                    ApplyProfile(profile, $"Hotkey {profile.Hotkey}");
                }
            }
        }

        base.WndProc(ref m);
    }

    private void BuildUi()
    {
        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10)
        };

        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 14));

        Controls.Add(rootLayout);

        var monitorGroup = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "Monitors (Auto = DDC/CI with fallback; OutputDisable is experimental)"
        };

        rootLayout.Controls.Add(monitorGroup, 0, 0);

        var monitorLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8)
        };

        monitorLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        monitorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        monitorGroup.Controls.Add(monitorLayout);

        ConfigureMonitorGrid();
        monitorLayout.Controls.Add(_monitorGrid, 0, 0);

        var monitorButtonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight
        };

        monitorLayout.Controls.Add(monitorButtonPanel, 0, 1);

        var refreshButton = new Button
        {
            Text = "Refresh Monitors",
            AutoSize = true
        };
        refreshButton.Click += (_, _) => RefreshMonitors();

        var applyCurrentButton = new Button
        {
            Text = "Apply Grid States",
            AutoSize = true
        };
        applyCurrentButton.Click += (_, _) => ApplyCurrentGridStates("Manual apply");

        var allOnButton = new Button
        {
            Text = "All On",
            AutoSize = true
        };
        allOnButton.Click += (_, _) =>
        {
            SetAllRowsState(true);
            ApplyCurrentGridStates("All On");
        };

        var allOffButton = new Button
        {
            Text = "All Off",
            AutoSize = true
        };
        allOffButton.Click += (_, _) =>
        {
            SetAllRowsState(false);
            ApplyCurrentGridStates("All Off");
        };

        var clearOverlaysButton = new Button
        {
            Text = "Clear Overlays",
            AutoSize = true
        };
        clearOverlaysButton.Click += (_, _) =>
        {
            _overlayController.RemoveOverlaysNotInSet(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            Log("Cleared blackout overlays.");
            SetStatus("Blackout overlays cleared.");
        };

        monitorButtonPanel.Controls.Add(refreshButton);
        monitorButtonPanel.Controls.Add(applyCurrentButton);
        monitorButtonPanel.Controls.Add(allOnButton);
        monitorButtonPanel.Controls.Add(allOffButton);
        monitorButtonPanel.Controls.Add(clearOverlaysButton);

        _brightnessLabel.Text = "Global Brightness: 50%";
        _brightnessLabel.AutoSize = true;
        _brightnessLabel.Margin = new Padding(10, 6, 0, 0);

        _brightnessTrackBar.Minimum = 0;
        _brightnessTrackBar.Maximum = 100;
        _brightnessTrackBar.Value = 50;
        _brightnessTrackBar.TickFrequency = 10;
        _brightnessTrackBar.Width = 200;
        _brightnessTrackBar.AutoSize = false;
        _brightnessTrackBar.Height = 30;
        
        _brightnessTrackBar.MouseCaptureChanged += (_, _) => ApplyGlobalBrightness();
        _brightnessTrackBar.KeyUp += (_, _) => ApplyGlobalBrightness();
        _brightnessTrackBar.ValueChanged += (_, _) => _brightnessLabel.Text = $"Global Brightness: {_brightnessTrackBar.Value}%";

        monitorButtonPanel.Controls.Add(_brightnessLabel);
        monitorButtonPanel.Controls.Add(_brightnessTrackBar);

        var profileGroup = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "Profiles and Global Hotkeys"
        };

        rootLayout.Controls.Add(profileGroup, 0, 1);

        var profileLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(8)
        };

        profileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        profileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        profileGroup.Controls.Add(profileLayout);

        var profileLeftPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };

        profileLeftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        profileLeftPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _profileList.Dock = DockStyle.Fill;
        _profileList.IntegralHeight = false;
        _profileList.SelectedIndexChanged += (_, _) => PopulateProfileEditorFromSelection();
        profileLeftPanel.Controls.Add(_profileList, 0, 0);

        var profileActionButtonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true
        };

        var applyProfileButton = new Button
        {
            Text = "Apply Selected Profile",
            AutoSize = true
        };
        applyProfileButton.Click += (_, _) => ApplySelectedProfile();

        var deleteProfileButton = new Button
        {
            Text = "Delete Selected Profile",
            AutoSize = true
        };
        deleteProfileButton.Click += (_, _) => DeleteSelectedProfile();

        var newProfileButton = new Button
        {
            Text = "New Profile",
            AutoSize = true
        };
        newProfileButton.Click += (_, _) =>
        {
            _profileList.ClearSelected();
            _profileNameTextBox.Text = $"Profile {_settings.Profiles.Count + 1}";
            SetHotkeyEditorSelection(new HotkeyBinding { Modifiers = HotkeyModifiers.Alt, Key = Keys.None });
        };

        profileActionButtonPanel.Controls.Add(applyProfileButton);
        profileActionButtonPanel.Controls.Add(deleteProfileButton);
        profileActionButtonPanel.Controls.Add(newProfileButton);
        profileLeftPanel.Controls.Add(profileActionButtonPanel, 0, 1);

        profileLayout.Controls.Add(profileLeftPanel, 0, 0);

        var profileEditorPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(0)
        };

        profileEditorPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        profileEditorPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        profileEditorPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        profileEditorPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        profileEditorPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        profileEditorPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        profileEditorPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var profileNameLabel = new Label
        {
            Text = "Profile Name",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };

        _profileNameTextBox.Dock = DockStyle.Fill;

        var modifierLabel = new Label
        {
            Text = "Modifiers",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };

        _modifierComboBox.Dock = DockStyle.Fill;
        _modifierComboBox.DropDownStyle = ComboBoxStyle.DropDownList;

        var keyLabel = new Label
        {
            Text = "Key",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };

        _keyComboBox.Dock = DockStyle.Fill;
        _keyComboBox.DropDownStyle = ComboBoxStyle.DropDownList;

        var saveProfileButton = new Button
        {
            Text = "Save Profile From Grid",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        saveProfileButton.Click += (_, _) => SaveProfileFromCurrentGridState();

        var profileHintLabel = new Label
        {
            Text = "Example: set Modifiers=Alt and Key=NumPad4 for Alt+Numpad4",
            AutoSize = true,
            Dock = DockStyle.Top
        };

        profileEditorPanel.Controls.Add(profileNameLabel, 0, 0);
        profileEditorPanel.Controls.Add(_profileNameTextBox, 1, 0);
        profileEditorPanel.Controls.Add(modifierLabel, 0, 1);
        profileEditorPanel.Controls.Add(_modifierComboBox, 1, 1);
        profileEditorPanel.Controls.Add(keyLabel, 0, 2);
        profileEditorPanel.Controls.Add(_keyComboBox, 1, 2);
        profileEditorPanel.Controls.Add(saveProfileButton, 1, 3);
        profileEditorPanel.Controls.Add(profileHintLabel, 1, 4);

        profileLayout.Controls.Add(profileEditorPanel, 1, 0);

        var bottomPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(4)
        };

        bottomPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bottomPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var statusPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight
        };

        _statusLabel.AutoSize = true;
        _statusLabel.Text = "Ready";
        _statusLabel.Margin = new Padding(3, 7, 16, 3);

        _darkModeCheckBox.AutoSize = true;
        _darkModeCheckBox.Text = "Dark mode";
        _darkModeCheckBox.CheckedChanged += (_, _) => OnDarkModeChanged();

        statusPanel.Controls.Add(_statusLabel);
        statusPanel.Controls.Add(_darkModeCheckBox);

        _logTextBox.Dock = DockStyle.Fill;
        _logTextBox.Multiline = true;
        _logTextBox.ReadOnly = true;
        _logTextBox.ScrollBars = ScrollBars.Vertical;

        bottomPanel.Controls.Add(statusPanel, 0, 0);
        bottomPanel.Controls.Add(_logTextBox, 0, 1);

        rootLayout.Controls.Add(bottomPanel, 0, 2);
    }

    private void ConfigureMonitorGrid()
    {
        _monitorGrid.Dock = DockStyle.Fill;
        _monitorGrid.AutoGenerateColumns = false;
        _monitorGrid.AllowUserToAddRows = false;
        _monitorGrid.AllowUserToDeleteRows = false;
        _monitorGrid.RowHeadersVisible = false;
        _monitorGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _monitorGrid.MultiSelect = false;

        var idColumn = new DataGridViewTextBoxColumn
        {
            HeaderText = "Id",
            DataPropertyName = nameof(MonitorRow.Id),
            Visible = false
        };

        var deviceColumn = new DataGridViewTextBoxColumn
        {
            HeaderText = "Display",
            DataPropertyName = nameof(MonitorRow.DeviceName),
            Width = 115,
            ReadOnly = true
        };

        var friendlyNameColumn = new DataGridViewTextBoxColumn
        {
            HeaderText = "Monitor",
            DataPropertyName = nameof(MonitorRow.FriendlyName),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            ReadOnly = false
        };

        var boundsColumn = new DataGridViewTextBoxColumn
        {
            HeaderText = "Bounds",
            DataPropertyName = nameof(MonitorRow.BoundsText),
            Width = 210,
            ReadOnly = true
        };

        var ddcColumn = new DataGridViewCheckBoxColumn
        {
            HeaderText = "DDC/CI",
            DataPropertyName = nameof(MonitorRow.SupportsPowerControl),
            Width = 60,
            ReadOnly = true
        };

        var modeColumn = new DataGridViewComboBoxColumn
        {
            HeaderText = "Mode",
            DataPropertyName = nameof(MonitorRow.Mode),
            Width = 145,
            DataSource = Enum.GetValues(typeof(MonitorControlMode))
        };

        var ddcTypeColumn = new DataGridViewComboBoxColumn
        {
            HeaderText = "DDC Type",
            DataPropertyName = nameof(MonitorRow.DdcBrightnessType),
            Width = 105,
            DataSource = Enum.GetValues(typeof(DdcBrightnessType))
        };

        var targetColumn = new DataGridViewCheckBoxColumn
        {
            HeaderText = "Signal On",
            DataPropertyName = nameof(MonitorRow.TargetOn),
            Width = 80
        };

        var offsetColumn = new DataGridViewTextBoxColumn
        {
            HeaderText = "Brightness Offset",
            DataPropertyName = nameof(MonitorRow.BrightnessOffset),
            Width = 110,
            DefaultCellStyle = new DataGridViewCellStyle { Format = "N0" }
        };

        _monitorGrid.Columns.Add(idColumn);
        _monitorGrid.Columns.Add(deviceColumn);
        _monitorGrid.Columns.Add(friendlyNameColumn);
        _monitorGrid.Columns.Add(boundsColumn);
        _monitorGrid.Columns.Add(ddcColumn);
        _monitorGrid.Columns.Add(modeColumn);
        _monitorGrid.Columns.Add(ddcTypeColumn);
        _monitorGrid.Columns.Add(targetColumn);
        _monitorGrid.Columns.Add(offsetColumn);

        _monitorGrid.DataSource = _monitorRows;
        _monitorGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_monitorGrid.IsCurrentCellDirty)
            {
                _monitorGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };

        _monitorGrid.CellValueChanged += (_, eventArgs) =>
        {
            if (eventArgs.RowIndex < 0 || eventArgs.ColumnIndex < 0)
            {
                return;
            }

            var changedColumn = _monitorGrid.Columns[eventArgs.ColumnIndex];

            if (string.Equals(changedColumn.DataPropertyName, nameof(MonitorRow.Mode), StringComparison.Ordinal))
            {
                SaveMonitorModesFromGrid(true);

                if (_monitorGrid.Rows[eventArgs.RowIndex].DataBoundItem is not MonitorRow row)
                {
                    return;
                }

                if (row.Mode == MonitorControlMode.DdcCi && !row.SupportsPowerControl)
                {
                    Log($"'{row.FriendlyName}' does not support DDC/CI. Use Auto, BlackoutOverlay, or OutputDisable mode.");
                }

                if (row.Mode == MonitorControlMode.OutputDisable)
                {
                    Log($"'{row.FriendlyName}' is using OutputDisable mode. Windows may temporarily rearrange windows for this display.");
                }
            }
            else if (string.Equals(changedColumn.DataPropertyName, nameof(MonitorRow.BrightnessOffset), StringComparison.Ordinal))
            {
                if (_monitorGrid.Rows[eventArgs.RowIndex].DataBoundItem is MonitorRow row)
                {
                    _settings.BrightnessOffsets[row.Id] = row.BrightnessOffset;
                    _settingsStore.Save(_settings);
                    ApplyGlobalBrightness(); // Live update
                }
            }
            else if (string.Equals(changedColumn.DataPropertyName, nameof(MonitorRow.FriendlyName), StringComparison.Ordinal))
            {
                if (_monitorGrid.Rows[eventArgs.RowIndex].DataBoundItem is MonitorRow row)
                {
                    if (string.IsNullOrWhiteSpace(row.FriendlyName))
                    {
                        // Remove custom name if cleared
                        _settings.CustomMonitorNames.Remove(row.Id);
                    }
                    else
                    {
                        _settings.CustomMonitorNames[row.Id] = row.FriendlyName;
                    }
                    _settingsStore.Save(_settings);
                }
            }
            else if (string.Equals(changedColumn.DataPropertyName, nameof(MonitorRow.DdcBrightnessType), StringComparison.Ordinal))
            {
                if (_monitorGrid.Rows[eventArgs.RowIndex].DataBoundItem is MonitorRow row)
                {
                    _settings.DdcBrightnessTypes[row.Id] = row.DdcBrightnessType;
                    _settingsStore.Save(_settings);
                    ApplyGlobalBrightness(); // Live update
                }
            }
        };

        _monitorGrid.DataError += (_, _) =>
        {
        };
    }

    private void PopulateHotkeyEditors()
    {
        _modifierComboBox.Items.Clear();
        _modifierComboBox.Items.Add(new ModifierChoice("None", HotkeyModifiers.None));
        _modifierComboBox.Items.Add(new ModifierChoice("Alt", HotkeyModifiers.Alt));
        _modifierComboBox.Items.Add(new ModifierChoice("Ctrl+Alt", HotkeyModifiers.Control | HotkeyModifiers.Alt));
        _modifierComboBox.Items.Add(new ModifierChoice("Shift+Alt", HotkeyModifiers.Shift | HotkeyModifiers.Alt));
        _modifierComboBox.Items.Add(new ModifierChoice("Ctrl+Shift+Alt", HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Alt));

        _keyComboBox.Items.Clear();
        _keyComboBox.Items.Add(new KeyChoice("None", Keys.None));

        _keyComboBox.Items.Add(new KeyChoice("NumPad0", Keys.NumPad0));
        _keyComboBox.Items.Add(new KeyChoice("NumPad1", Keys.NumPad1));
        _keyComboBox.Items.Add(new KeyChoice("NumPad2", Keys.NumPad2));
        _keyComboBox.Items.Add(new KeyChoice("NumPad3", Keys.NumPad3));
        _keyComboBox.Items.Add(new KeyChoice("NumPad4", Keys.NumPad4));
        _keyComboBox.Items.Add(new KeyChoice("NumPad5", Keys.NumPad5));
        _keyComboBox.Items.Add(new KeyChoice("NumPad6", Keys.NumPad6));
        _keyComboBox.Items.Add(new KeyChoice("NumPad7", Keys.NumPad7));
        _keyComboBox.Items.Add(new KeyChoice("NumPad8", Keys.NumPad8));
        _keyComboBox.Items.Add(new KeyChoice("NumPad9", Keys.NumPad9));

        _modifierComboBox.SelectedIndex = 1;
        _keyComboBox.SelectedIndex = 0;
    }

    private void RefreshMonitors()
    {
        var existingStates = _monitorRows.ToDictionary(row => row.Id, row => row.TargetOn, StringComparer.OrdinalIgnoreCase);
        var existingModes = _monitorRows.ToDictionary(row => row.Id, row => row.Mode, StringComparer.OrdinalIgnoreCase);

        var descriptors = _monitorController.RefreshMonitors();

        _monitorRows.Clear();

        var activeMonitorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in descriptors)
        {
            activeMonitorIds.Add(descriptor.Id);

            var targetOn = existingStates.TryGetValue(descriptor.Id, out var previousTarget)
                ? previousTarget
                : descriptor.IsOn ?? true;

            var mode = ResolveModeForDescriptor(descriptor, existingModes);

            var offset = _settings.BrightnessOffsets.TryGetValue(descriptor.Id, out var existingOffset) ? existingOffset : 0;
            var ddcType = _settings.DdcBrightnessTypes.TryGetValue(descriptor.Id, out var existingDdcType) ? existingDdcType : DdcBrightnessType.Luminance;
            var friendlyName = _settings.CustomMonitorNames.TryGetValue(descriptor.Id, out var customName) && !string.IsNullOrWhiteSpace(customName)
                ? customName
                : descriptor.FriendlyName;

            _monitorRows.Add(new MonitorRow
            {
                Id = descriptor.Id,
                DeviceName = descriptor.DeviceName,
                FriendlyName = friendlyName,
                BoundsText = FormatBounds(descriptor.Bounds),
                Bounds = descriptor.Bounds,
                SupportsPowerControl = descriptor.SupportsPowerControl,
                Mode = mode,
                DdcBrightnessType = ddcType,
                BrightnessOffset = offset,
                TargetOn = targetOn
            });
        }

        _overlayController.RemoveOverlaysNotInSet(activeMonitorIds);
        SaveMonitorModesFromGrid(false);

        if (_monitorRows.Count == 0)
        {
            UpdateEmergencyWakeArmState();
            Log("No monitors were discovered.");
            SetStatus("No monitors found.");
            return;
        }

        var unsupportedCount = _monitorRows.Count(row => !row.SupportsPowerControl);
        SetStatus($"Discovered {_monitorRows.Count} monitor entries.");

        if (unsupportedCount > 0)
        {
            Log($"{unsupportedCount} monitor(s) do not expose DDC/CI. Use Auto, BlackoutOverlay, or OutputDisable mode for them.");
        }

        UpdateEmergencyWakeArmState();
    }

    private void ApplyCurrentGridStates(string source)
    {
        if (_monitorRows.Count == 0)
        {
            SetStatus("No monitors to apply.");
            return;
        }

        SaveMonitorModesFromGrid(false);

        var successCount = 0;
        var failureCount = 0;
        var processedOutputDisableDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in _monitorRows)
        {
            if (row.Mode == MonitorControlMode.OutputDisable)
            {
                if (!processedOutputDisableDevices.Add(row.DeviceName))
                {
                    continue;
                }

                if (HasConflictingOutputDisableStatesForDevice(row.DeviceName))
                {
                    failureCount++;
                    Log($"{row.DeviceName}: OutputDisable rows for this display conflict. Set all related rows to the same Signal On value.");
                    continue;
                }
            }

            var result = ApplyMonitorRowState(row);

            if (result.Success)
            {
                successCount++;
            }
            else
            {
                failureCount++;
            }

            if (result.ShouldLog)
            {
                Log($"{row.FriendlyName}: {result.Message}");
            }
        }

        _settingsStore.Save(_settings);
        SetStatus($"{source}: {successCount} succeeded, {failureCount} failed.");
        UpdateEmergencyWakeArmState();
    }

    private void SaveProfileFromCurrentGridState()
    {
        if (_monitorRows.Count == 0)
        {
            MessageBox.Show(this, "No monitor state is available to save.", "No Monitors", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var profileName = _profileNameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(profileName))
        {
            MessageBox.Show(this, "Enter a profile name.", "Profile Name Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selectedHotkey = BuildHotkeyFromEditor();
        var selectedProfile = _profileList.SelectedItem as MonitorProfile;

        var profile = selectedProfile;

        if (profile is null)
        {
            profile = _settings.Profiles.FirstOrDefault(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase));

            if (profile is null)
            {
                profile = new MonitorProfile();
                _settings.Profiles.Add(profile);
            }
        }

        var conflict = _settings.Profiles.FirstOrDefault(existing =>
            !ReferenceEquals(existing, profile) &&
            HotkeysEqual(existing.Hotkey, selectedHotkey));

        if (conflict is not null)
        {
            MessageBox.Show(
                this,
                $"The shortcut {selectedHotkey} is already used by profile '{conflict.Name}'.",
                "Hotkey Conflict",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        profile.Name = profileName;
        profile.Hotkey = selectedHotkey;
        profile.MonitorStates = _monitorRows.ToDictionary(row => row.Id, row => row.TargetOn, StringComparer.OrdinalIgnoreCase);

        SaveMonitorModesFromGrid(false);
        _settingsStore.Save(_settings);
        ReloadProfileList(profile.Name);
        RegisterProfileHotkeys();

        Log($"Saved profile '{profile.Name}' with {profile.MonitorStates.Count} monitor state(s).");
        SetStatus($"Saved profile '{profile.Name}'.");
    }

    private void ApplySelectedProfile()
    {
        if (_profileList.SelectedItem is not MonitorProfile profile)
        {
            MessageBox.Show(this, "Select a profile first.", "No Profile Selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplyProfile(profile, "Manual profile apply");
    }

    private void ApplyProfile(MonitorProfile profile, string source)
    {
        foreach (var row in _monitorRows)
        {
            if (profile.MonitorStates.TryGetValue(row.Id, out var desiredState))
            {
                row.TargetOn = desiredState;
            }
        }

        _monitorGrid.Refresh();
        ApplyCurrentGridStates($"{source}: {profile.Name}");
    }

    private void DeleteSelectedProfile()
    {
        if (_profileList.SelectedItem is not MonitorProfile selectedProfile)
        {
            return;
        }

        var confirmResult = MessageBox.Show(
            this,
            $"Delete profile '{selectedProfile.Name}'?",
            "Confirm Deletion",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirmResult != DialogResult.Yes)
        {
            return;
        }

        _settings.Profiles.Remove(selectedProfile);
        _settingsStore.Save(_settings);

        ReloadProfileList();
        RegisterProfileHotkeys();

        Log($"Deleted profile '{selectedProfile.Name}'.");
        SetStatus("Profile deleted.");
    }

    private void PopulateProfileEditorFromSelection()
    {
        if (_profileList.SelectedItem is not MonitorProfile profile)
        {
            return;
        }

        _profileNameTextBox.Text = profile.Name;
        SetHotkeyEditorSelection(profile.Hotkey);
    }

    private void ReloadProfileList(string? selectedProfileName = null)
    {
        _profileList.Items.Clear();

        foreach (var profile in _settings.Profiles.OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase))
        {
            _profileList.Items.Add(profile);
        }

        if (_profileList.Items.Count == 0)
        {
            _profileNameTextBox.Text = "Profile 1";
            SetHotkeyEditorSelection(new HotkeyBinding { Modifiers = HotkeyModifiers.Alt, Key = Keys.None });
            return;
        }

        if (!string.IsNullOrWhiteSpace(selectedProfileName))
        {
            for (var index = 0; index < _profileList.Items.Count; index++)
            {
                if (_profileList.Items[index] is MonitorProfile profile &&
                    string.Equals(profile.Name, selectedProfileName, StringComparison.OrdinalIgnoreCase))
                {
                    _profileList.SelectedIndex = index;
                    return;
                }
            }
        }

        _profileList.SelectedIndex = 0;
    }

    private void RegisterProfileHotkeys()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        UnregisterProfileHotkeys();

        _nextHotkeyId = 1;

        foreach (var profile in _settings.Profiles)
        {
            var hotkey = profile.Hotkey;

            if (hotkey is null || !hotkey.IsAssigned)
            {
                continue;
            }

            var id = _nextHotkeyId++;
            var success = NativeMethods.RegisterHotKey(Handle, id, (uint)hotkey.Modifiers, (uint)hotkey.Key);

            if (success)
            {
                _registeredHotkeys[id] = profile.Name;
            }
            else
            {
                var win32Error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                Log($"Failed to register hotkey {hotkey} for profile '{profile.Name}' (Win32: {win32Error}).");
            }
        }
    }

    private void EnsureGlobalKeyboardHook()
    {
        if (_globalKeyboardHook is not null)
        {
            return;
        }

        try
        {
            _globalKeyboardHook = new GlobalKeyboardHook(OnGlobalKeyboardKeyDown);
            Log("Global any-key emergency wake hook is active.");
        }
        catch (Exception ex)
        {
            Log($"Failed to activate global keyboard hook: {ex.Message}");
        }
    }

    private void DisposeGlobalKeyboardHook()
    {
        _globalKeyboardHook?.Dispose();
        _globalKeyboardHook = null;
    }

    private void OnGlobalKeyboardKeyDown()
    {
        if (!_emergencyWakeArmed)
        {
            return;
        }

        // Ignore keydown events that happen immediately after arming to avoid instant re-wake.
        if (DateTime.UtcNow - _emergencyWakeArmedAtUtc < EmergencyWakeActivationDelay)
        {
            return;
        }

        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(EmergencyWakeAllFromAnyKey);
            return;
        }

        EmergencyWakeAllFromAnyKey();
    }

    private void EmergencyWakeAllFromAnyKey()
    {
        if (!_emergencyWakeArmed)
        {
            return;
        }

        Log("Emergency wake triggered by global key press.");
        SetAllRowsState(true);
        ApplyCurrentGridStates("Emergency wake");
    }

    private void UnregisterProfileHotkeys()
    {
        foreach (var hotkeyId in _registeredHotkeys.Keys)
        {
            if (IsHandleCreated)
            {
                NativeMethods.UnregisterHotKey(Handle, hotkeyId);
            }
        }

        _registeredHotkeys.Clear();
    }

    private void UpdateEmergencyWakeArmState()
    {
        var shouldArm = _monitorRows.Count > 0 && _monitorRows.All(row => !row.TargetOn);

        if (shouldArm == _emergencyWakeArmed)
        {
            return;
        }

        _emergencyWakeArmed = shouldArm;

        if (_emergencyWakeArmed)
        {
            _emergencyWakeArmedAtUtc = DateTime.UtcNow;
            Log("Emergency wake armed: press any key to wake all monitors.");
            SetStatus("All monitors are off. Press any key to wake all monitors.");
            return;
        }

        Log("Emergency wake disarmed.");
    }

    private void SetAllRowsState(bool on)
    {
        foreach (var row in _monitorRows)
        {
            row.TargetOn = on;
        }

        _monitorGrid.Refresh();
    }

    private MonitorApplyResult ApplyMonitorRowState(MonitorRow row)
    {
        return row.Mode switch
        {
            MonitorControlMode.DdcCi => ApplyViaDdc(row, allowAutoFallback: false),
            MonitorControlMode.BlackoutOverlay => ApplyViaBlackoutOverlay(row),
            MonitorControlMode.OutputDisable => ApplyViaOutputDisable(row),
            MonitorControlMode.Auto => ApplyViaAutoMode(row),
            MonitorControlMode.SoftwareDimming => ApplyViaSoftwareDimming(row),
            _ => MonitorApplyResult.Fail($"Unsupported mode: {row.Mode}")
        };
    }

    private MonitorApplyResult ApplyViaDdc(MonitorRow row, bool allowAutoFallback)
    {
        if (!row.SupportsPowerControl)
        {
            return MonitorApplyResult.Fail("This monitor does not support DDC/CI power control.");
        }

        _overlayController.SetOverlay(row.Id, row.Bounds, false);

        var ddcResult = _monitorController.SetPowerState(row.Id, row.TargetOn);

        if (ddcResult.Success)
        {
            return MonitorApplyResult.Ok($"Applied via DDC/CI -> {(row.TargetOn ? "On" : "Off")}.", false);
        }

        if (allowAutoFallback && !row.TargetOn)
        {
            _overlayController.SetOverlay(row.Id, row.Bounds, true);
            return MonitorApplyResult.Ok(
                "DDC/CI failed while turning off; applied blackout overlay fallback instead.",
                true);
        }

        return MonitorApplyResult.Fail(ddcResult.ErrorMessage);
    }

    private MonitorApplyResult ApplyViaBlackoutOverlay(MonitorRow row)
    {
        _overlayController.SetOverlay(row.Id, row.Bounds, !row.TargetOn);

        return row.TargetOn
            ? MonitorApplyResult.Ok("Blackout overlay removed.", false)
            : MonitorApplyResult.Ok("Blackout overlay applied.", false);
    }

    private MonitorApplyResult ApplyViaSoftwareDimming(MonitorRow row)
    {
        if (!row.TargetOn)
        {
            _overlayController.SetOverlay(row.Id, row.Bounds, show: true);
            return MonitorApplyResult.Ok("Software dimming monitor turned off via blackout.", false);
        }

        var offset = _settings.BrightnessOffsets.TryGetValue(row.Id, out var existingOffset) ? existingOffset : 0;
        var value = (int)_brightnessTrackBar.Value;
        var targetBrightness = Math.Clamp(value + offset, 0, 100);
        double opacity = (100 - targetBrightness) / 100.0 * 0.95;

        _overlayController.SetOverlay(row.Id, row.Bounds, show: true, opacity: opacity);
        return MonitorApplyResult.Ok($"Software dimming overlay applied.", false);
    }

    private MonitorApplyResult ApplyViaOutputDisable(MonitorRow row)
    {
        _overlayController.SetOverlay(row.Id, row.Bounds, false);

        var outputResult = _displayOutputController.SetOutputEnabled(row.DeviceName, row.TargetOn);

        if (!outputResult.Success)
        {
            return MonitorApplyResult.Fail(outputResult.Message);
        }

        return MonitorApplyResult.Ok(outputResult.Message, true);
    }

    private MonitorApplyResult ApplyViaAutoMode(MonitorRow row)
    {
        if (row.TargetOn)
        {
            _overlayController.SetOverlay(row.Id, row.Bounds, false);

            if (!row.SupportsPowerControl)
            {
                return MonitorApplyResult.Ok("Set to On by clearing blackout overlay.", false);
            }

            var onResult = _monitorController.SetPowerState(row.Id, true);

            return onResult.Success
                ? MonitorApplyResult.Ok("Monitor powered on via DDC/CI.", false)
                : MonitorApplyResult.Fail(onResult.ErrorMessage);
        }

        if (row.SupportsPowerControl)
        {
            return ApplyViaDdc(row, allowAutoFallback: true);
        }

        _overlayController.SetOverlay(row.Id, row.Bounds, true);
        return MonitorApplyResult.Ok("Monitor does not support DDC/CI. Applied blackout overlay fallback.", true);
    }

    private bool HasConflictingOutputDisableStatesForDevice(string deviceName)
    {
        var distinctStates = _monitorRows
            .Where(row => row.Mode == MonitorControlMode.OutputDisable &&
                          string.Equals(row.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
            .Select(row => row.TargetOn)
            .Distinct()
            .Take(2)
            .Count();

        return distinctStates > 1;
    }

    private HotkeyBinding? BuildHotkeyFromEditor()
    {
        var selectedModifier = _modifierComboBox.SelectedItem as ModifierChoice;
        var selectedKey = _keyComboBox.SelectedItem as KeyChoice;

        if (selectedModifier is null || selectedKey is null || selectedKey.Value == Keys.None)
        {
            return null;
        }

        return new HotkeyBinding
        {
            Modifiers = selectedModifier.Value,
            Key = selectedKey.Value
        };
    }

    private void SetHotkeyEditorSelection(HotkeyBinding? hotkey)
    {
        var modifierValue = hotkey?.Modifiers ?? HotkeyModifiers.Alt;
        var keyValue = hotkey?.Key ?? Keys.None;

        for (var index = 0; index < _modifierComboBox.Items.Count; index++)
        {
            if (_modifierComboBox.Items[index] is ModifierChoice modifierChoice && modifierChoice.Value == modifierValue)
            {
                _modifierComboBox.SelectedIndex = index;
                break;
            }
        }

        for (var index = 0; index < _keyComboBox.Items.Count; index++)
        {
            if (_keyComboBox.Items[index] is KeyChoice keyChoice && keyChoice.Value == keyValue)
            {
                _keyComboBox.SelectedIndex = index;
                break;
            }
        }
    }

    private void OnDarkModeChanged()
    {
        _settings.DarkMode = _darkModeCheckBox.Checked;
        ApplyTheme(_settings.DarkMode);
        _settingsStore.Save(_settings);
    }

    private void ApplyTheme(bool darkMode)
    {
        var palette = darkMode ? ThemePalette.Dark : ThemePalette.Light;
        ApplyThemeToControl(this, palette);
        ApplyGridTheme(palette);
        ApplyWindowChromeTheme(darkMode);
    }

    private void ApplyWindowChromeTheme(bool darkMode)
    {
        if (!IsHandleCreated)
        {
            return;
        }

        var useDarkMode = darkMode ? 1 : 0;

        _ = NativeMethods.DwmSetWindowAttribute(
            Handle,
            NativeMethods.DwmwaUseImmersiveDarkMode,
            ref useDarkMode,
            sizeof(int));

        _ = NativeMethods.DwmSetWindowAttribute(
            Handle,
            NativeMethods.DwmwaUseImmersiveDarkModeBefore20h1,
            ref useDarkMode,
            sizeof(int));

        if (darkMode)
        {
            uint captionColor = 0x001F1A18;
            uint textColor = 0x00F2ECE8;
            uint borderColor = 0x003E3730;

            _ = NativeMethods.DwmSetWindowAttribute(
                Handle,
                NativeMethods.DwmwaCaptionColor,
                ref captionColor,
                sizeof(uint));

            _ = NativeMethods.DwmSetWindowAttribute(
                Handle,
                NativeMethods.DwmwaTextColor,
                ref textColor,
                sizeof(uint));

            _ = NativeMethods.DwmSetWindowAttribute(
                Handle,
                NativeMethods.DwmwaBorderColor,
                ref borderColor,
                sizeof(uint));

            return;
        }

        uint defaultColor = NativeMethods.DwmColorDefault;

        _ = NativeMethods.DwmSetWindowAttribute(
            Handle,
            NativeMethods.DwmwaCaptionColor,
            ref defaultColor,
            sizeof(uint));

        _ = NativeMethods.DwmSetWindowAttribute(
            Handle,
            NativeMethods.DwmwaTextColor,
            ref defaultColor,
            sizeof(uint));

        _ = NativeMethods.DwmSetWindowAttribute(
            Handle,
            NativeMethods.DwmwaBorderColor,
            ref defaultColor,
            sizeof(uint));
    }

    private void ApplyThemeToControl(Control control, ThemePalette palette)
    {
        if (control is DataGridView)
        {
            return;
        }

        switch (control)
        {
            case Form:
                control.BackColor = palette.FormBack;
                control.ForeColor = palette.Text;
                break;

            case GroupBox:
                control.BackColor = palette.FormBack;
                control.ForeColor = palette.Text;
                break;

            case TableLayoutPanel:
            case FlowLayoutPanel:
            case Panel:
                control.BackColor = palette.PanelBack;
                control.ForeColor = palette.Text;
                break;

            case Label:
            case CheckBox:
                control.BackColor = palette.PanelBack;
                control.ForeColor = palette.Text;
                break;

            case Button button:
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = palette.Border;
                button.BackColor = palette.ButtonBack;
                button.ForeColor = palette.Text;
                break;

            case TextBox textBox:
                textBox.BorderStyle = BorderStyle.FixedSingle;
                textBox.BackColor = textBox.ReadOnly ? palette.PanelBack : palette.InputBack;
                textBox.ForeColor = palette.Text;
                break;

            case ComboBox comboBox:
                comboBox.BackColor = palette.InputBack;
                comboBox.ForeColor = palette.Text;
                break;

            case ListBox listBox:
                listBox.BackColor = palette.InputBack;
                listBox.ForeColor = palette.Text;
                listBox.BorderStyle = BorderStyle.FixedSingle;
                break;

            default:
                control.BackColor = palette.PanelBack;
                control.ForeColor = palette.Text;
                break;
        }

        foreach (Control child in control.Controls)
        {
            ApplyThemeToControl(child, palette);
        }
    }

    private void ApplyGridTheme(ThemePalette palette)
    {
        _monitorGrid.EnableHeadersVisualStyles = false;
        _monitorGrid.BackgroundColor = palette.PanelBack;
        _monitorGrid.GridColor = palette.Border;
        _monitorGrid.BorderStyle = BorderStyle.FixedSingle;

        _monitorGrid.ColumnHeadersDefaultCellStyle.BackColor = palette.HeaderBack;
        _monitorGrid.ColumnHeadersDefaultCellStyle.ForeColor = palette.Text;
        _monitorGrid.ColumnHeadersDefaultCellStyle.SelectionBackColor = palette.HeaderBack;
        _monitorGrid.ColumnHeadersDefaultCellStyle.SelectionForeColor = palette.Text;

        _monitorGrid.DefaultCellStyle.BackColor = palette.InputBack;
        _monitorGrid.DefaultCellStyle.ForeColor = palette.Text;
        _monitorGrid.DefaultCellStyle.SelectionBackColor = palette.SelectionBack;
        _monitorGrid.DefaultCellStyle.SelectionForeColor = palette.Text;

        _monitorGrid.AlternatingRowsDefaultCellStyle.BackColor = palette.AltRowBack;
        _monitorGrid.AlternatingRowsDefaultCellStyle.ForeColor = palette.Text;
        _monitorGrid.AlternatingRowsDefaultCellStyle.SelectionBackColor = palette.SelectionBack;
        _monitorGrid.AlternatingRowsDefaultCellStyle.SelectionForeColor = palette.Text;
    }

    private void SaveMonitorModesFromGrid(bool persist)
    {
        foreach (var row in _monitorRows)
        {
            _settings.MonitorModes[row.Id] = row.Mode;
        }

        if (persist)
        {
            _settingsStore.Save(_settings);
        }
    }

    private MonitorControlMode ResolveModeForDescriptor(
        MonitorDescriptor descriptor,
        IReadOnlyDictionary<string, MonitorControlMode> inMemoryModes)
    {
        if (inMemoryModes.TryGetValue(descriptor.Id, out var mode))
        {
            return mode;
        }

        if (_settings.MonitorModes.TryGetValue(descriptor.Id, out mode))
        {
            return mode;
        }

        return descriptor.SupportsPowerControl
            ? MonitorControlMode.Auto
            : MonitorControlMode.BlackoutOverlay;
    }

    private void ApplyGlobalBrightness()
    {
        var value = (uint)_brightnessTrackBar.Value;
        Log($"Applying global brightness: {value}%");
        _monitorController.SetGlobalBrightness(value, _settings.BrightnessOffsets, _settings.DdcBrightnessTypes);

        _brightnessTrayIcon?.UpdateBrightness((int)value);

        foreach (var row in _monitorRows)
        {
            if (row.Mode == MonitorControlMode.SoftwareDimming && row.TargetOn)
            {
                var offset = _settings.BrightnessOffsets.TryGetValue(row.Id, out var existingOffset) ? existingOffset : 0;
                var targetBrightness = Math.Clamp((int)value + offset, 0, 100);
                double opacity = (100 - targetBrightness) / 100.0 * 0.95;
                _overlayController.SetOverlay(row.Id, row.Bounds, show: true, opacity: opacity);
            }
        }
    }

    private void SyncBrightnessFromMonitors()
    {
        if (_brightnessTrackBar.Capture || ContainsFocus)
        {
            // Do not sync while the user is interacting with the window to avoid jitter.
            return;
        }

        var currentBrightness = _monitorController.GetInitialBrightness(_settings.BrightnessOffsets, _settings.DdcBrightnessTypes);
        if (currentBrightness.HasValue)
        {
            var value = (int)currentBrightness.Value;
            if (value >= 0 && value <= 100 && _brightnessTrackBar.Value != value)
            {
                _brightnessTrackBar.Value = value;
                _brightnessLabel.Text = $"Global Brightness: {value}%";
                _brightnessTrayIcon?.UpdateBrightness(value);
            }
        }
    }

    private void StartVirtualDriverIpcClient()
    {
        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using var pipeClient = new NamedPipeClientStream(".", "VirtualMonitorBrightnessPipe", PipeDirection.In);
                    await pipeClient.ConnectAsync();

                    using var reader = new BinaryReader(pipeClient);
                    while (pipeClient.IsConnected)
                    {
                        var targetBrightness = reader.ReadInt32();
                        
                        Invoke(() =>
                        {
                            if (targetBrightness >= 0 && targetBrightness <= 100)
                            {
                                _brightnessTrackBar.Value = targetBrightness;
                                ApplyGlobalBrightness();
                            }
                        });
                    }
                }
                catch
                {
                    // Ignore errors, pipe might not be available or driver not running.
                    await Task.Delay(5000);
                }
            }
        });
    }

    private void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _logTextBox.AppendText(line + Environment.NewLine);
    }

    private void SetStatus(string status)
    {
        _statusLabel.Text = status;
    }

    private static bool HotkeysEqual(HotkeyBinding? left, HotkeyBinding? right)
    {
        if (left is null && right is null)
        {
            return false;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.Modifiers == right.Modifiers && left.Key == right.Key;
    }

    private static string FormatBounds(Rectangle bounds)
    {
        return $"X:{bounds.X}, Y:{bounds.Y}, {bounds.Width}x{bounds.Height}";
    }

    private sealed class MonitorRow
    {
        public required string Id { get; set; }

        public required string DeviceName { get; set; }

        public required string FriendlyName { get; set; }

        public required string BoundsText { get; set; }

        public required Rectangle Bounds { get; set; }

        public required bool SupportsPowerControl { get; set; }

        public required MonitorControlMode Mode { get; set; }

        public DdcBrightnessType DdcBrightnessType { get; set; }

        public int BrightnessOffset { get; set; }

        public bool TargetOn { get; set; }
    }

    private sealed class MonitorApplyResult
    {
        public required bool Success { get; init; }

        public required string Message { get; init; }

        public required bool ShouldLog { get; init; }

        public static MonitorApplyResult Ok(string message, bool shouldLog)
        {
            return new MonitorApplyResult
            {
                Success = true,
                Message = message,
                ShouldLog = shouldLog
            };
        }

        public static MonitorApplyResult Fail(string message)
        {
            return new MonitorApplyResult
            {
                Success = false,
                Message = message,
                ShouldLog = true
            };
        }
    }

    private sealed class ModifierChoice
    {
        public ModifierChoice(string label, HotkeyModifiers value)
        {
            Label = label;
            Value = value;
        }

        public string Label { get; }

        public HotkeyModifiers Value { get; }

        public override string ToString() => Label;
    }

    private sealed class KeyChoice
    {
        public KeyChoice(string label, Keys value)
        {
            Label = label;
            Value = value;
        }

        public string Label { get; }

        public Keys Value { get; }

        public override string ToString() => Label;
    }

    private sealed class ThemePalette
    {
        public required Color FormBack { get; init; }

        public required Color PanelBack { get; init; }

        public required Color InputBack { get; init; }

        public required Color ButtonBack { get; init; }

        public required Color HeaderBack { get; init; }

        public required Color AltRowBack { get; init; }

        public required Color Border { get; init; }

        public required Color SelectionBack { get; init; }

        public required Color Text { get; init; }

        public static ThemePalette Dark => new()
        {
            FormBack = Color.FromArgb(24, 26, 31),
            PanelBack = Color.FromArgb(30, 33, 39),
            InputBack = Color.FromArgb(37, 40, 47),
            ButtonBack = Color.FromArgb(47, 52, 61),
            HeaderBack = Color.FromArgb(44, 49, 58),
            AltRowBack = Color.FromArgb(33, 36, 43),
            Border = Color.FromArgb(82, 89, 104),
            SelectionBack = Color.FromArgb(70, 94, 132),
            Text = Color.FromArgb(232, 236, 242)
        };

        public static ThemePalette Light => new()
        {
            FormBack = Color.FromArgb(244, 246, 250),
            PanelBack = Color.White,
            InputBack = Color.White,
            ButtonBack = Color.FromArgb(232, 237, 246),
            HeaderBack = Color.FromArgb(224, 231, 241),
            AltRowBack = Color.FromArgb(248, 250, 253),
            Border = Color.FromArgb(180, 188, 201),
            SelectionBack = Color.FromArgb(188, 212, 242),
            Text = Color.FromArgb(28, 35, 44)
        };
    }
}
