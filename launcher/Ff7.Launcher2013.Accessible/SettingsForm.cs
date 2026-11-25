using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FF7_Launcher;

namespace BlindSoldier.Ff7Launcher2013
{
	// The settings the original 2013 driver reads from Documents. Display
	// options appear only for that driver; FFNx keeps its own FFNx.toml. Every
	// control is named by its visible label and the sliders expose their value.
	internal class SettingsForm : Form
	{
		private readonly LauncherText text;
		private readonly SettingsEditRequest request;
		private readonly bool showDisplay;
		private Label effectsValue;
		private Label musicValue;
		private int nextTabIndex;

		internal SettingsForm(LauncherText text, SettingsEditRequest request)
		{
			this.text = text ?? throw new ArgumentNullException(nameof(text));
			this.request = request ?? throw new ArgumentNullException(nameof(request));
			showDisplay = request.Installation?.Driver?.Kind == LegacyDriverKind.Original;

			SuspendLayout();
			Text = text[TextKey.SettingsTitle];
			Name = "LegacyLauncherSettings";
			AutoScaleMode = AutoScaleMode.Font;
			Font = SystemFonts.MessageBoxFont;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MinimizeBox = false;
			MaximizeBox = false;
			ShowInTaskbar = false;
			StartPosition = FormStartPosition.CenterParent;
			AutoSize = true;
			AutoSizeMode = AutoSizeMode.GrowAndShrink;

			var layout = new FlowLayoutPanel
			{
				Name = "SettingsLayout",
				AccessibleName = string.Empty,
				FlowDirection = FlowDirection.TopDown,
				WrapContents = false,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				Padding = new Padding(12),
				TabIndex = 0
			};

			var notes = new List<string>();
			if (showDisplay)
			{
				if (request.DisplayState == LegacySettingsFileState.Unreadable)
				{
					notes.Add(text[TextKey.DisplayUnreadableNote]);
				}
				layout.Controls.Add(CreateDisplayGroup(request.Display ?? LegacyDisplaySettings.CreateDefault()));
			}
			else if (request.Installation?.Driver?.Kind == LegacyDriverKind.Ffnx)
			{
				notes.Add(text[TextKey.FfnxDisplayNote]);
			}
			else
			{
				notes.Add(text[TextKey.UnrecognizedDisplayNote]);
			}
			layout.Controls.Add(CreateSoundGroup(request.Sound ?? LegacySoundSettings.CreateDefault()));
			if (showDisplay && request.Profiles != null && request.Profiles.Count > 1)
			{
				layout.Controls.Add(CreateSaveFolderPanel());
			}
			notes.Add(text[TextKey.ControlsNote]);
			layout.Controls.Add(CreateNotesPanel(notes));
			layout.Controls.Add(CreateButtonsPanel());
			Controls.Add(layout);
			ResumeLayout(true);
		}

		internal CheckBox FullscreenBox { get; private set; }

		internal ComboBox ResolutionBox { get; private set; }

		internal ComboBox RefreshBox { get; private set; }

		internal CheckBox KeepAspectBox { get; private set; }

		internal CheckBox LinearFilteringBox { get; private set; }

		internal CheckBox OriginalModeBox { get; private set; }

		internal TrackBar EffectsSlider { get; private set; }

		internal TrackBar MusicSlider { get; private set; }

		internal ComboBox SaveFolderBox { get; private set; }

		internal TextBox NotesBox { get; private set; }

		internal Button OkButton { get; private set; }

		internal Button CancelButtonControl { get; private set; }

		internal SettingsEditResult BuildResult()
		{
			LegacyDisplaySettings display = null;
			if (showDisplay)
			{
				display = (request.Display ?? LegacyDisplaySettings.CreateDefault()).Clone();
				var mode = (SizeItem)ResolutionBox.SelectedItem;
				var rate = (RateItem)RefreshBox.SelectedItem;
				display.Width = mode.Width;
				display.Height = mode.Height;
				display.RefreshRate = rate.Rate;
				display.Fullscreen = FullscreenBox.Checked;
				display.KeepAspectRatio = KeepAspectBox.Checked;
				display.LinearFiltering = LinearFilteringBox.Checked;
				display.OriginalMode = OriginalModeBox.Checked;
			}

			return new SettingsEditResult
			{
				Display = display,
				Sound = new LegacySoundSettings
				{
					SoundEffectsVolume = (uint)EffectsSlider.Value,
					MusicVolume = (uint)MusicSlider.Value
				},
				SelectedProfile = SaveFolderBox != null ? ((ProfileItem)SaveFolderBox.SelectedItem).Profile : request.SelectedProfile
			};
		}

		private int NextTab()
		{
			return nextTabIndex++;
		}

		private Control CreateDisplayGroup(LegacyDisplaySettings display)
		{
			var group = CreateGroup("DisplayGroup", text[TextKey.DisplayGroup]);
			var panel = (TableLayoutPanel)group.Controls[0];

			FullscreenBox = CreateCheckBox("FullscreenBox", TextKey.Fullscreen, display.Fullscreen);
			panel.Controls.Add(FullscreenBox);
			panel.SetColumnSpan(FullscreenBox, 2);

			ResolutionBox = CreateComboBox("ResolutionBox");
			AddLabelled(panel, TextKey.Resolution, ResolutionBox);
			RefreshBox = CreateComboBox("RefreshBox");
			AddLabelled(panel, TextKey.RefreshRate, RefreshBox);

			KeepAspectBox = CreateCheckBox("KeepAspectBox", TextKey.KeepAspect, display.KeepAspectRatio);
			LinearFilteringBox = CreateCheckBox("LinearFilteringBox", TextKey.LinearFiltering, display.LinearFiltering);
			OriginalModeBox = CreateCheckBox("OriginalModeBox", TextKey.OriginalMode, display.OriginalMode);
			foreach (CheckBox box in new[] { KeepAspectBox, LinearFilteringBox, OriginalModeBox })
			{
				panel.Controls.Add(box);
				panel.SetColumnSpan(box, 2);
			}

			PopulateResolutions(display);
			PopulateRates((SizeItem)ResolutionBox.SelectedItem, display.RefreshRate);
			ResolutionBox.SelectedIndexChanged += delegate
			{
				uint current = RefreshBox.SelectedItem is RateItem rate ? rate.Rate : 0;
				PopulateRates((SizeItem)ResolutionBox.SelectedItem, current);
			};
			return group;
		}

		private Control CreateSoundGroup(LegacySoundSettings sound)
		{
			var group = CreateGroup("SoundGroup", text[TextKey.SoundGroup]);
			var panel = (TableLayoutPanel)group.Controls[0];
			panel.ColumnCount = 3;
			EffectsSlider = CreateSlider("EffectsSlider", sound.SoundEffectsVolume);
			effectsValue = CreateValueLabel(EffectsSlider);
			AddLabelled(panel, TextKey.SoundEffectsVolume, EffectsSlider, effectsValue);
			MusicSlider = CreateSlider("MusicSlider", sound.MusicVolume);
			musicValue = CreateValueLabel(MusicSlider);
			AddLabelled(panel, TextKey.MusicVolume, MusicSlider, musicValue);
			return group;
		}

		private Control CreateSaveFolderPanel()
		{
			var panel = CreateTable("SaveFolderPanel");
			SaveFolderBox = CreateComboBox("SaveFolderBox");
			foreach (LegacySaveProfile profile in request.Profiles)
			{
				SaveFolderBox.Items.Add(new ProfileItem(profile, text.Format(TextKey.SaveFolderItem, profile.Name, profile.SaveFileCount)));
			}
			int selected = request.Profiles.ToList().FindIndex(profile => ReferenceEquals(profile, request.SelectedProfile) ||
				(request.SelectedProfile != null && string.Equals(profile.Name, request.SelectedProfile.Name, StringComparison.OrdinalIgnoreCase)));
			SaveFolderBox.SelectedIndex = selected >= 0 ? selected : 0;
			AddLabelled(panel, TextKey.SaveFolder, SaveFolderBox);
			return panel;
		}

		private Control CreateNotesPanel(IEnumerable<string> notes)
		{
			var panel = CreateTable("NotesPanel");
			var label = new Label
			{
				Name = "NotesLabel",
				Text = text[TextKey.NotesLabel],
				AutoSize = true,
				UseMnemonic = false,
				TabIndex = NextTab()
			};
			NotesBox = new TextBox
			{
				Name = "NotesBox",
				ReadOnly = true,
				Multiline = true,
				WordWrap = true,
				TabStop = true,
				BackColor = SystemColors.Window,
				Width = 520,
				Height = 80,
				ScrollBars = ScrollBars.Vertical,
				Text = string.Join(Environment.NewLine, notes),
				TabIndex = NextTab()
			};
			SettingsAccessibility.NameControl(NotesBox, label);
			panel.Controls.Add(label);
			panel.SetColumnSpan(label, 2);
			panel.Controls.Add(NotesBox);
			panel.SetColumnSpan(NotesBox, 2);
			return panel;
		}

		private Control CreateButtonsPanel()
		{
			var buttons = new FlowLayoutPanel
			{
				Name = "SettingsButtons",
				AccessibleName = string.Empty,
				FlowDirection = FlowDirection.RightToLeft,
				AutoSize = true,
				Anchor = AnchorStyles.Right,
				TabIndex = NextTab()
			};
			CancelButtonControl = new AccessibleSettingsButton
			{
				Name = "CancelButton",
				Text = text[TextKey.Cancel],
				AutoSize = true,
				MinimumSize = new Size(90, 30),
				DialogResult = DialogResult.Cancel,
				TabIndex = 1
			};
			OkButton = new AccessibleSettingsButton
			{
				Name = "OkButton",
				Text = text[TextKey.Ok],
				AutoSize = true,
				MinimumSize = new Size(90, 30),
				DialogResult = DialogResult.OK,
				TabIndex = 0
			};
			SettingsAccessibility.NameButton(OkButton);
			SettingsAccessibility.NameButton(CancelButtonControl);
			buttons.Controls.Add(CancelButtonControl);
			buttons.Controls.Add(OkButton);
			AcceptButton = OkButton;
			CancelButton = CancelButtonControl;
			return buttons;
		}

		private GroupBox CreateGroup(string name, string caption)
		{
			var group = new GroupBox
			{
				Name = name,
				Text = caption,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				Padding = new Padding(9),
				TabIndex = NextTab()
			};
			group.Controls.Add(CreateTable(name + "Layout"));
			return group;
		}

		private TableLayoutPanel CreateTable(string name)
		{
			// An explicit empty name stops Windows Forms from naming the
			// container after the label that precedes it in the Tab order.
			var panel = new TableLayoutPanel
			{
				Name = name,
				AccessibleName = string.Empty,
				ColumnCount = 2,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				Dock = DockStyle.Fill,
				TabIndex = NextTab()
			};
			return panel;
		}

		private CheckBox CreateCheckBox(string name, TextKey key, bool value)
		{
			var box = new CheckBox
			{
				Name = name,
				Text = text[key],
				AutoSize = true,
				Checked = value,
				UseMnemonic = true,
				TabIndex = NextTab()
			};
			box.AccessibleName = SettingsAccessibility.NormalizeLabel(box.Text);
			return box;
		}

		private ComboBox CreateComboBox(string name)
		{
			return new ComboBox
			{
				Name = name,
				DropDownStyle = ComboBoxStyle.DropDownList,
				Width = 380,
				TabIndex = 0
			};
		}

		private TrackBar CreateSlider(string name, uint value)
		{
			return new AccessibleSettingsTrackBar
			{
				Name = name,
				Minimum = 0,
				Maximum = (int)LegacySoundSettings.MaximumVolume,
				SmallChange = 1,
				LargeChange = 10,
				TickFrequency = 10,
				Value = (int)Math.Min(value, LegacySoundSettings.MaximumVolume),
				Width = 300,
				TabIndex = 0
			};
		}

		private Label CreateValueLabel(TrackBar slider)
		{
			var label = new Label
			{
				Name = slider.Name + "Value",
				AutoSize = true,
				UseMnemonic = false,
				Anchor = AnchorStyles.Left,
				Text = text.Format(TextKey.VolumeValue, slider.Value)
			};
			slider.ValueChanged += delegate { label.Text = text.Format(TextKey.VolumeValue, slider.Value); };
			return label;
		}

		// The visible label precedes its control in the Tab order, so the
		// label's mnemonic moves focus to the control it names.
		private void AddLabelled(TableLayoutPanel panel, TextKey key, Control control, Control trailing = null)
		{
			var label = new Label
			{
				Name = control.Name + "Label",
				Text = text[key],
				AutoSize = true,
				UseMnemonic = true,
				Anchor = AnchorStyles.Left,
				TabIndex = NextTab()
			};
			control.TabIndex = NextTab();
			SettingsAccessibility.NameControl(control, label);
			panel.Controls.Add(label);
			panel.Controls.Add(control);
			if (trailing != null)
			{
				trailing.TabIndex = NextTab();
				panel.Controls.Add(trailing);
			}
		}

		private void PopulateResolutions(LegacyDisplaySettings display)
		{
			var sizes = new List<SizeItem>();
			foreach (DisplayMode mode in request.Modes ?? Array.Empty<DisplayMode>())
			{
				if (!sizes.Any(item => item.Width == mode.Width && item.Height == mode.Height))
				{
					sizes.Add(new SizeItem(mode.Width, mode.Height, mode.RefreshRates, text.Format(TextKey.ResolutionItem, mode.Width, mode.Height)));
				}
			}
			bool savedListed = display.Width == 0 && display.Height == 0 ||
				sizes.Any(item => item.Width == display.Width && item.Height == display.Height);
			if (!savedListed)
			{
				sizes.Add(new SizeItem(display.Width, display.Height, Array.Empty<uint>(), text.Format(TextKey.ResolutionItem, display.Width, display.Height)));
			}

			ResolutionBox.Items.Add(new SizeItem(0, 0, Array.Empty<uint>(), text[TextKey.ResolutionDefault]));
			foreach (SizeItem item in sizes.OrderBy(item => item.Width).ThenBy(item => item.Height))
			{
				ResolutionBox.Items.Add(item);
			}
			ResolutionBox.SelectedItem = ResolutionBox.Items.Cast<SizeItem>()
				.First(item => item.Width == display.Width && item.Height == display.Height);
		}

		private void PopulateRates(SizeItem size, uint preferred)
		{
			RefreshBox.BeginUpdate();
			RefreshBox.Items.Clear();
			RefreshBox.Items.Add(new RateItem(0, text[TextKey.RefreshDefault]));
			// The saved rate stays available for the saved size even when this
			// monitor does not list it, so confirming Settings never changes it.
			var rates = new List<uint>(size.RefreshRates);
			uint savedRate = (request.Display ?? LegacyDisplaySettings.CreateDefault()).RefreshRate;
			if (IsSavedSize(size) && savedRate != 0 && !rates.Contains(savedRate))
			{
				rates.Add(savedRate);
			}
			foreach (uint rate in rates.Distinct().OrderBy(rate => rate))
			{
				RefreshBox.Items.Add(new RateItem(rate, text.Format(TextKey.RefreshItem, rate)));
			}
			RateItem match = RefreshBox.Items.Cast<RateItem>().FirstOrDefault(item => item.Rate == preferred);
			RefreshBox.SelectedItem = match ?? RefreshBox.Items[0];
			RefreshBox.EndUpdate();
		}

		private bool IsSavedSize(SizeItem size)
		{
			LegacyDisplaySettings display = request.Display ?? LegacyDisplaySettings.CreateDefault();
			return size.Width == display.Width && size.Height == display.Height;
		}

		private sealed class SizeItem
		{
			private readonly string caption;

			internal SizeItem(uint width, uint height, IReadOnlyList<uint> refreshRates, string caption)
			{
				Width = width;
				Height = height;
				RefreshRates = refreshRates;
				this.caption = caption;
			}

			internal uint Width { get; }

			internal uint Height { get; }

			internal IReadOnlyList<uint> RefreshRates { get; }

			public override string ToString() => caption;
		}

		private sealed class RateItem
		{
			private readonly string caption;

			internal RateItem(uint rate, string caption)
			{
				Rate = rate;
				this.caption = caption;
			}

			internal uint Rate { get; }

			public override string ToString() => caption;
		}

		private sealed class ProfileItem
		{
			private readonly string caption;

			internal ProfileItem(LegacySaveProfile profile, string caption)
			{
				Profile = profile;
				this.caption = caption;
			}

			internal LegacySaveProfile Profile { get; }

			public override string ToString() => caption;
		}
	}

	internal sealed class SettingsPresenter : ISettingsPresenter
	{
		public SettingsEditResult Edit(IWin32Window owner, LauncherText text, SettingsEditRequest request)
		{
			using (var form = new SettingsForm(text, request))
			{
				return form.ShowDialog(owner) == DialogResult.OK ? form.BuildResult() : null;
			}
		}
	}
}
