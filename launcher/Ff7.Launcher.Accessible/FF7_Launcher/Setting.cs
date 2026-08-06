#define TRACE
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using FF7_Launcher.Properties;
using SharpDX.XInput;

namespace FF7_Launcher;

public class Setting : Form
{
	public enum ControlType
	{
		None = -1,
		Language,
		MasterVolume,
		Resolution,
		ScreenMode,
		Display,
		OkButton,
		CancelButton,
		ApplyButton,
		ControlNum
	}

	private enum ControllerCategory
	{
		None,
		Keyboard,
		Joypad
	}

	public int m_LangIndexOld = -1;

	public int m_ResolutionIndexOld = -1;

	public int m_ScreenModeIndexOld = -1;

	private ApplicableCombobox DisplayApplicableBox;

	public FF7Launcher m_Launcher = null;

	public List<Resolution> m_ResolutionList;

	public int controlIndex = 0;

	private InputManager inputManager;

	private DXInputManager DXInput;

	private float MasterVolume = 0f;

	private float oldMasterVolume = 0f;

	private float MasterVolumeRateMax = 2f;

	private float MasterVolumeRateAccel = 1f / 30f;

	private float MasterVolumeRate = 0f;

	private MasterVolumeInput KeyBoardMasterVolumeInput;

	private ControllerCategory ActiveController = ControllerCategory.None;

	private List<Control> ControlList;

	private DInput dinput = null;

	private bool KeyPressing = false;

	private bool IsFirstTick = true;

	private bool bComboBoxDroppedDown = false;

	private LauncherSpeech AccessibilitySpeech;

	private bool AccessibilityReady = false;

	private bool SuppressAccessibilityAnnouncements = false;

	private volatile bool Running = false;

	private const int waitComboBoxKeyRepeatMax = 4;

	private int waitComboBoxKeyRepeat = 4;

	private IContainer components = null;

	private ComboBox languageBox;

	private Label languageLbl;

	private Label textLbl;

	private Label audioLbl;

	private Label masterVolumeLbl;

	private Label volMinusLbl;

	private TrackBar volTrackBar;

	private Label volPlusLbl;

	private Label volNumLbl;

	private Label graphicLbl;

	private Label resolutionLbl;

	private ComboBox resolutionBox;

	private Button okBtn;

	private Button cancelBtn;

	private Button applyBtn;

	private ComboBox screenModeBox;

	private Label screenModeLbl;

	private PictureBox VolIcon;

	private Label DisplayLbl;

	private ComboBox DisplayComboBox;

	public bool IsActive => Form.ActiveForm == this;

	public Setting()
	{
		InitializeComponent();
		SetupLanguageUI();
		SetupAudioUI();
		SetupGraphicUI();
		IEnumerable<uint> source = from i in Display.DisplayIDList()
			select i + 1;
		object[] items = source.Cast<object>().ToArray();
		DisplayApplicableBox = new ApplicableCombobox(DisplayComboBox, items, FF7Launcher.configFile.ShowDisplay);
		KeyBoardMasterVolumeInput = new MasterVolumeInput(volTrackBar);
		KeyBoardMasterVolumeInput.OnTick += KeyBoardMasterVolumeInput_OnTick;
		volTrackBar.KeyDown += VolTrackBar_KeyDown;
		volTrackBar.KeyUp += VolTrackBar_KeyUp;
		base.KeyPreview = true;
		base.KeyDown += Setting_KeyDown;
		base.KeyUp += Setting_KeyUp;
		languageBox.KeyDown += OnKeyDown;
		resolutionBox.KeyDown += OnKeyDown;
		screenModeBox.KeyDown += OnKeyDown;
		DisplayApplicableBox.Box.KeyDown += OnKeyDown;
		okBtn.PreviewKeyDown += OkBtn_PreviewKeyDown;
		cancelBtn.PreviewKeyDown += OkBtn_PreviewKeyDown;
		applyBtn.PreviewKeyDown += OkBtn_PreviewKeyDown;
		ControlList = new List<Control> { languageBox, volTrackBar, resolutionBox, screenModeBox, DisplayComboBox, okBtn, cancelBtn, applyBtn };
		applyBtn.Enabled = false;
		Apply();
		RefreshAccessibilityMetadata();
		AttachAccessibilityHandlers();
		base.Shown += Setting_Shown;
	}

	internal void AttachAccessibilitySpeech(LauncherSpeech speech)
	{
		AccessibilitySpeech = speech;
	}

	private void AttachAccessibilityHandlers()
	{
		foreach (Control control in ControlList)
		{
			control.Enter += AccessibilityControl_Enter;
		}
		languageBox.SelectedIndexChanged += AccessibilityControl_ValueChanged;
		resolutionBox.SelectedIndexChanged += AccessibilityControl_ValueChanged;
		screenModeBox.SelectedIndexChanged += AccessibilityControl_ValueChanged;
		DisplayComboBox.SelectedIndexChanged += AccessibilityControl_ValueChanged;
		volTrackBar.ValueChanged += AccessibilityControl_ValueChanged;
		applyBtn.EnabledChanged += AccessibilityControl_ValueChanged;
	}

	private void Setting_Shown(object sender, EventArgs e)
	{
		AccessibilityReady = true;
		AccessibilitySpeech?.ResetDeduplication();
		Control current = ActiveControl ?? languageBox;
		string description = SettingsAccessibility.Describe(current);
		AccessibilitySpeech?.Speak(Text + ". " + description, interrupt: true);
	}

	private void AccessibilityControl_Enter(object sender, EventArgs e)
	{
		Control control = sender as Control;
		int index = control == null ? -1 : ControlList.IndexOf(control);
		if (index >= 0)
		{
			controlIndex = index;
		}
		AnnounceAccessibilityControl(control);
	}

	private void AccessibilityControl_ValueChanged(object sender, EventArgs e)
	{
		Control control = sender as Control;
		if (control != null && (control.ContainsFocus || (controlIndex >= 0 && controlIndex < ControlList.Count && ControlList[controlIndex] == control)))
		{
			AnnounceAccessibilityControl(control);
		}
	}

	private void AnnounceAccessibilityControl(Control control)
	{
		if (!AccessibilityReady || SuppressAccessibilityAnnouncements || AccessibilitySpeech == null || control == null)
		{
			return;
		}
		AccessibilitySpeech.Speak(SettingsAccessibility.Describe(control), interrupt: true);
	}

	private void RefreshAccessibilityMetadata()
	{
		SettingsAccessibility.NameControl(languageBox, languageLbl);
		SettingsAccessibility.NameControl(volTrackBar, masterVolumeLbl);
		SettingsAccessibility.NameControl(resolutionBox, resolutionLbl);
		SettingsAccessibility.NameControl(screenModeBox, screenModeLbl);
		SettingsAccessibility.NameControl(DisplayComboBox, DisplayLbl);
		SettingsAccessibility.NameButton(okBtn);
		SettingsAccessibility.NameButton(cancelBtn);
		SettingsAccessibility.NameButton(applyBtn);
	}

	private void LoopOkToApply(int offset)
	{
		int count = ControlList.Count;
		controlIndex += offset;
		Trace.WriteLine("controlIndex : " + controlIndex);
		bool enabled = applyBtn.Enabled;
		int num = (enabled ? count : (count - 1));
		int num2 = (enabled ? 1 : 2);
		if (controlIndex == num)
		{
			controlIndex = count - 3;
			Trace.WriteLine("warp ok ");
		}
		else if (controlIndex == count - 4)
		{
			controlIndex = count - num2;
			Trace.WriteLine("warp apply ");
		}
		ControlList[controlIndex].Focus();
	}

	private void OkBtn_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
	{
		if (KeyPressing)
		{
			e.IsInputKey = true;
			return;
		}
		bool flag = e.Control && e.KeyCode == Keys.Tab;
		bool flag2 = e.KeyCode == Keys.Tab;
		if (!(flag || flag2))
		{
			Trace.WriteLine("OkBtn_PreviewKeyDown : !keypress");
			ProcessUpdown(e.KeyCode);
			ProcessLeftRight(e.KeyCode);
			Control control = (Control)sender;
			e.IsInputKey = true;
		}
	}

	private void OnKeyDown(object sender, KeyEventArgs e)
	{
		ComboBox comboBox = (ComboBox)sender;
		if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
		{
			ChangeFocusedComboBoxSelection(comboBox, e.KeyCode == Keys.Up ? -1 : 1);
			e.Handled = true;
			e.SuppressKeyPress = true;
			return;
		}
		if (e.KeyCode == Keys.Right)
		{
			Invoke_ApplyInputEvent_Right();
			e.Handled = true;
			e.SuppressKeyPress = true;
		}
		else if (e.KeyCode == Keys.Left)
		{
			Invoke_ApplyInputEvent_Left();
			e.Handled = true;
			e.SuppressKeyPress = true;
		}
	}

	private void Setting_KeyUp(object sender, KeyEventArgs e)
	{
		Trace.WriteLine("KeyUpped\n");
		KeyPressing = false;
	}

	private void Setting_KeyDown(object sender, KeyEventArgs e)
	{
		Trace.WriteLine("key : " + e.KeyCode);
		if ((ActiveControl is ComboBox && (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)) ||
			(ActiveControl == volTrackBar && (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down || e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)))
		{
			return;
		}
		if (e.KeyCode == Keys.Return || e.KeyCode == Keys.X)
		{
			ApplyInputEvent_Select();
			KeyPressing = true;
		}
		if (e.KeyCode == Keys.Back || e.KeyCode == Keys.C)
		{
			ApplyInputEvent_Cancel();
			KeyPressing = true;
		}
		if (!CheckComboBoxDroppedDown())
		{
			ProcessUpdown(e.KeyCode);
		}
	}

	private void ProcessUpdown(Keys key)
	{
		if (key == Keys.Up && !KeyPressing)
		{
			ApplyInputEvent_UpDown(-1);
			KeyPressing = true;
		}
		if (key == Keys.Down && !KeyPressing)
		{
			ApplyInputEvent_UpDown(1);
			KeyPressing = true;
		}
	}

	private void ProcessLeftRight(Keys key)
	{
		if (key == Keys.Right && !KeyPressing)
		{
			LoopOkToApply(1);
			Trace.WriteLine("ProcessLeftRight : KeyPressing");
			KeyPressing = true;
		}
		if (key == Keys.Left && !KeyPressing)
		{
			LoopOkToApply(-1);
			Trace.WriteLine("ProcessLeftRight : KeyPressing");
			KeyPressing = true;
		}
	}

	private void KeyBoardMasterVolumeInput_OnTick(object sender, EventArgs e)
	{
		if (IsFirstTick)
		{
			IsFirstTick = false;
			SuppressAccessibilityAnnouncements = true;
			try
			{
				SendKeys.Send("{TAB}");
				SendKeys.Send("+{TAB}");
			}
			finally
			{
				SuppressAccessibilityAnnouncements = false;
			}
		}
		volTrackBar.Value = (int)KeyBoardMasterVolumeInput.MasterVolume;
		volNumLbl.Text = volTrackBar.Value.ToString();
		if (ActiveController != ControllerCategory.Joypad && KeyBoardMasterVolumeInput.IsActive)
		{
			ActiveController = ControllerCategory.Keyboard;
		}
		if (ActiveController == ControllerCategory.Keyboard && !KeyBoardMasterVolumeInput.IsActive)
		{
			ActiveController = ControllerCategory.None;
		}
		if (ActiveController == ControllerCategory.Joypad)
		{
			KeyBoardMasterVolumeInput.Disable();
		}
		if (ActiveController == ControllerCategory.None)
		{
			KeyBoardMasterVolumeInput.Enable();
		}
	}

	private void VolTrackBar_KeyDown(object sender, KeyEventArgs e)
	{
		int offset = ((e.KeyCode == Keys.Up || e.KeyCode == Keys.Right) ? 1 : ((e.KeyCode == Keys.Down || e.KeyCode == Keys.Left) ? (-1) : 0));
		if (offset != 0)
		{
			ChangeTrackBarValue(offset);
			e.Handled = true;
			e.SuppressKeyPress = true;
		}
	}

	private void VolTrackBar_KeyUp(object sender, KeyEventArgs e)
	{
		KeyBoardMasterVolumeInput.OnKey(e, keydown: false);
		e.Handled = true;
	}

	public void StartInput(InputManager inputManager, DXInputManager dXInput)
	{
		if (inputManager.xbcontroller.IsConnected)
		{
			this.inputManager = inputManager;
			StartXInputLoop();
		}
		else
		{
			dinput = dXInput.DirectInput;
			StartDInputLoop(dinput);
		}
		DXInput = new DXInputManager(dinput, inputManager);
	}

	private void SoundIconDebugger_OnImageChanged(object sender, ImageChangedEventArgs e)
	{
		VolIcon.Image = e.Image;
	}

	private void SetupLanguageUI()
	{
		languageBox.Items.Add("日本語");
		languageBox.Items.Add("English");
		languageBox.Items.Add("Français");
		languageBox.Items.Add("Deutsch");
		languageBox.Items.Add("Español");
		if (FF7Launcher.languageInstalled == "jp")
		{
			languageBox.SelectedIndex = 0;
		}
		else if (FF7Launcher.languageInstalled == "en")
		{
			languageBox.SelectedIndex = 1;
		}
		else if (FF7Launcher.languageInstalled == "fr")
		{
			languageBox.SelectedIndex = 2;
		}
		else if (FF7Launcher.languageInstalled == "de")
		{
			languageBox.SelectedIndex = 3;
		}
		else if (FF7Launcher.languageInstalled == "es")
		{
			languageBox.SelectedIndex = 4;
		}
		m_LangIndexOld = languageBox.SelectedIndex;
	}

	private void SetupAudioUI()
	{
		volTrackBar.Minimum = 0;
		volTrackBar.Maximum = 100;
		volTrackBar.Value = FF7Launcher.configFile.m_masterVolume;
		volTrackBar.TickFrequency = 10;
		volTrackBar.SmallChange = 1;
		volTrackBar.LargeChange = 1;
		volNumLbl.Text = volTrackBar.Value.ToString();
		MasterVolume = volTrackBar.Value;
		oldMasterVolume = MasterVolume;
		volTrackBar.ValueChanged += VolTrackBar_ValueChanged;
	}

	private void VolTrackBar_ValueChanged(object sender, EventArgs e)
	{
		MasterVolume = volTrackBar.Value;
		KeyBoardMasterVolumeInput.UpdateMasterVolume((int)MasterVolume);
	}

	private void SetupGraphicUI()
	{
		SetupResolutionComboBox();
		int w = FF7Launcher.configFile.m_width;
		int h = FF7Launcher.configFile.m_height;
		int resolutionComboBoxIndex = GetResolutionComboBoxIndex(w, h);
		if (resolutionComboBoxIndex < resolutionBox.Items.Count)
		{
			resolutionBox.SelectedIndex = resolutionComboBoxIndex;
			m_ResolutionIndexOld = resolutionBox.SelectedIndex;
		}
		else
		{
			resolutionBox.SelectedIndex = 0;
			m_ResolutionIndexOld = resolutionBox.SelectedIndex;
		}
		Trace.WriteLine("languageInstalled : " + FF7Launcher.languageInstalled);
		LocalizeUI();
		if (FF7Launcher.configFile.m_screenMode >= 0 && FF7Launcher.configFile.m_screenMode < 3)
		{
			screenModeBox.SelectedIndex = FF7Launcher.configFile.m_screenMode;
			m_ScreenModeIndexOld = screenModeBox.SelectedIndex;
		}
	}

	private void LocalizeUI()
	{
		ComponentResourceManager componentResourceManager = new ComponentResourceManager(typeof(Setting));
		Text = componentResourceManager.GetString("Setting.Text");
		if (screenModeBox.Items.Count == 0)
		{
			screenModeBox.Items.Add(componentResourceManager.GetString("FullScreen"));
			screenModeBox.Items.Add(componentResourceManager.GetString("Borderless"));
			screenModeBox.Items.Add(componentResourceManager.GetString("Windowed"));
		}
		else
		{
			screenModeBox.Items[0] = componentResourceManager.GetString("FullScreen");
			screenModeBox.Items[1] = componentResourceManager.GetString("Borderless");
			screenModeBox.Items[2] = componentResourceManager.GetString("Windowed");
		}
	}

	private void SetupResolutionComboBox()
	{
		m_ResolutionList = Display.GetSortedResolutionList(FF7Launcher.configFile.ShowDisplay);
		resolutionBox.Items.Clear();
		foreach (Resolution resolution in m_ResolutionList)
		{
			int num = resolution.Size.x;
			int num2 = resolution.Size.y;
			string item = $"{num}x{num2}";
			resolutionBox.Items.Add(item);
		}
	}

	private int GetResolutionComboBoxIndex(int w, int h)
	{
		int num = 0;
		foreach (Resolution resolution in m_ResolutionList)
		{
			if (w == resolution.Size.x && h == resolution.Size.y)
			{
				break;
			}
			num++;
		}
		return num;
	}

	private void Apply()
	{
		string cultureCode = "ja";
		switch (languageBox.SelectedIndex)
		{
		case 0:
			cultureCode = "ja";
			FF7Launcher.languageInstalled = "jp";
			break;
		case 1:
			cultureCode = "en";
			FF7Launcher.languageInstalled = "en";
			break;
		case 2:
			cultureCode = "fr";
			FF7Launcher.languageInstalled = "fr";
			break;
		case 3:
			cultureCode = "de";
			FF7Launcher.languageInstalled = "de";
			break;
		case 4:
			cultureCode = "es";
			FF7Launcher.languageInstalled = "es";
			break;
		}
		m_LangIndexOld = languageBox.SelectedIndex;
		RuntimeLocalizer.ChangeCulture(this, cultureCode);
		volNumLbl.Text = volTrackBar.Value.ToString();
		LocalizeUI();
		RefreshAccessibilityMetadata();
		if (m_Launcher != null)
		{
			RuntimeLocalizer.ChangeCulture(m_Launcher, cultureCode);
		}
		if (m_ScreenModeIndexOld != screenModeBox.SelectedIndex)
		{
			m_ScreenModeIndexOld = screenModeBox.SelectedIndex;
		}
		FF7Launcher.configFile.m_masterVolume = volTrackBar.Value;
		FF7Launcher.configFile.SetLanguage(languageBox.SelectedIndex);
		int selectedIndex = resolutionBox.SelectedIndex;
		if (selectedIndex >= 0 && selectedIndex < m_ResolutionList.Count)
		{
			FF7Launcher.configFile.m_width = m_ResolutionList[selectedIndex].Size.x;
			FF7Launcher.configFile.m_height = m_ResolutionList[selectedIndex].Size.y;
		}
		if (screenModeBox.SelectedIndex >= 0 && screenModeBox.SelectedIndex < 3)
		{
			FF7Launcher.configFile.m_screenMode = screenModeBox.SelectedIndex;
		}
		FF7Launcher.configFile.ShowDisplay = DisplayApplicableBox.SelectedIndex;
		DisplayApplicableBox.Apply();
		SetupGraphicUI();
		FF7Launcher.configFile.Write("config.txt");
	}

	private void okBtn_Click(object sender, EventArgs e)
	{
		Apply();
		Close();
	}

	private void cancelBtn_Click(object sender, EventArgs e)
	{
		Close();
	}

	private void applyBtn_Click(object sender, EventArgs e)
	{
		Apply();
		applyBtn.Enabled = false;
	}

	private void volTrackBar_ValueChanged(object sender, EventArgs e)
	{
		volNumLbl.Text = volTrackBar.Value.ToString();
		applyBtn.Enabled = true;
	}

	private void volIcon_Click(object sender, EventArgs e)
	{
	}

	private void languageBox_SelectedIndexChanged(object sender, EventArgs e)
	{
		Console.WriteLine($"languageBox.SelectedIndex {languageBox.SelectedIndex}");
		if (m_LangIndexOld != languageBox.SelectedIndex)
		{
			applyBtn.Enabled = true;
		}
	}

	private void resolutionBox_SelectedIndexChanged(object sender, EventArgs e)
	{
		if (m_ResolutionIndexOld != resolutionBox.SelectedIndex)
		{
			applyBtn.Enabled = true;
		}
	}

	private void screenModeBox_SelectedIndexChanged(object sender, EventArgs e)
	{
		if (m_ScreenModeIndexOld != screenModeBox.SelectedIndex)
		{
			applyBtn.Enabled = true;
		}
	}

	private void Setting_Activated(object sender, EventArgs e)
	{
	}

	public bool ApplyInput()
	{
		if (m_Launcher == null)
		{
			return false;
		}
		if (m_Launcher.inputManager == null)
		{
			return false;
		}
		bool result = false;
		bool flag = false;
		bool flag2 = false;
		bool flag3 = false;
		bool flag4 = false;
		if (inputManager.ControllerStickJustPressed(GamepadKeyCode.LeftThumbUp))
		{
			flag = true;
		}
		else if (inputManager.ControllerStickJustPressed(GamepadKeyCode.LeftThumbDown))
		{
			flag2 = true;
		}
		if (inputManager.ControllerStickJustPressed(GamepadKeyCode.LeftThumbRight))
		{
			flag3 = true;
		}
		else if (inputManager.ControllerStickJustPressed(GamepadKeyCode.LeftThumbLeft))
		{
			flag4 = true;
		}
		if (m_Launcher.inputManager.ControllerButtonJustPressed(GamepadButtonFlags.DPadUp) || flag)
		{
			if (Invoke_CheckComboBoxDroppedDown())
			{
				Invoke_ApplyInputEvent_Left();
			}
			else
			{
				Invoke_ApplyInputEvent_UpDown(-1);
			}
			result = true;
		}
		else if (m_Launcher.inputManager.ControllerButtonJustPressed(GamepadButtonFlags.DPadDown) || flag2)
		{
			if (Invoke_CheckComboBoxDroppedDown())
			{
				Invoke_ApplyInputEvent_Right();
			}
			else
			{
				Invoke_ApplyInputEvent_UpDown(1);
			}
			result = true;
		}
		else if (m_Launcher.inputManager.ControllerButtonJustPressed(GamepadButtonFlags.DPadRight) || flag3)
		{
			switch (controlIndex)
			{
			case 5:
				Invoke_ApplyInputEvent_UpDown(1);
				break;
			case 6:
				if (applyBtn.Enabled)
				{
					Invoke_ApplyInputEvent_UpDown(1);
				}
				else
				{
					Invoke_ApplyInputEvent_UpDown(-1);
				}
				break;
			case 7:
				Invoke_ApplyInputEvent_UpDown(-1);
				Invoke_ApplyInputEvent_UpDown(-1, playSE: false);
				break;
			default:
				Invoke_ApplyInputEvent_Right();
				break;
			}
			result = true;
		}
		else if (m_Launcher.inputManager.ControllerButtonJustPressed(GamepadButtonFlags.DPadLeft) || flag4)
		{
			switch (controlIndex)
			{
			case 5:
				if (applyBtn.Enabled)
				{
					Invoke_ApplyInputEvent_UpDown(1);
					Invoke_ApplyInputEvent_UpDown(1, playSE: false);
				}
				else
				{
					Invoke_ApplyInputEvent_UpDown(1);
				}
				break;
			case 6:
			case 7:
				Invoke_ApplyInputEvent_UpDown(-1);
				break;
			default:
				Invoke_ApplyInputEvent_Left();
				break;
			}
			result = true;
		}
		else if (ProcessDXInput())
		{
			result = true;
		}
		return result;
	}

	private bool Invoke_CheckComboBoxDroppedDown()
	{
		try
		{
			if (base.InvokeRequired)
			{
				Invoke((Action)delegate
				{
					Invoke_CheckComboBoxDroppedDown();
				});
			}
			else
			{
				bComboBoxDroppedDown = CheckComboBoxDroppedDown();
			}
		}
		catch
		{
		}
		return bComboBoxDroppedDown;
	}

	private bool CheckComboBoxDroppedDown()
	{
		if (languageBox.DroppedDown || resolutionBox.DroppedDown || screenModeBox.DroppedDown || DisplayApplicableBox.DroppedDown)
		{
			return true;
		}
		return false;
	}

	public void ApplyInputRepeat()
	{
		bool flag = false;
		bool flag2 = false;
		bool flag3 = false;
		bool flag4 = false;
		int fwd = 0;
		if (inputManager.ControllerStickPressed(GamepadKeyCode.LeftThumbUp))
		{
			flag = true;
			fwd = -1;
		}
		else if (inputManager.ControllerStickPressed(GamepadKeyCode.LeftThumbDown))
		{
			flag2 = true;
			fwd = 1;
		}
		if (inputManager.ControllerStickPressed(GamepadKeyCode.LeftThumbRight))
		{
			flag3 = true;
			fwd = 1;
		}
		else if (inputManager.ControllerStickPressed(GamepadKeyCode.LeftThumbLeft))
		{
			flag4 = true;
			fwd = -1;
		}
		GamepadButtonFlags btnFlag = GamepadButtonFlags.None;
		if (m_Launcher.inputManager.ControllerButtonPressed(GamepadButtonFlags.DPadUp) || flag)
		{
			btnFlag = GamepadButtonFlags.DPadUp;
			fwd = -1;
		}
		else if (m_Launcher.inputManager.ControllerButtonPressed(GamepadButtonFlags.DPadDown) || flag2)
		{
			btnFlag = GamepadButtonFlags.DPadDown;
			fwd = 1;
		}
		if (m_Launcher.inputManager.ControllerButtonPressed(GamepadButtonFlags.DPadRight) || flag3)
		{
			btnFlag = GamepadButtonFlags.DPadRight;
			fwd = 1;
		}
		else if (m_Launcher.inputManager.ControllerButtonPressed(GamepadButtonFlags.DPadLeft) || flag4)
		{
			btnFlag = GamepadButtonFlags.DPadLeft;
			fwd = -1;
		}
		if (controlIndex == 1)
		{
			Invoke_MasterVolume(btnFlag);
			return;
		}
		int num = controlIndex;
		int num2 = num;
		if (num2 == 0 || (uint)(num2 - 2) <= 2u)
		{
			Invoke_ComboBoxKeyRepeat(btnFlag, fwd);
		}
	}

	private void Invoke_MasterVolume(GamepadButtonFlags btnFlag)
	{
		try
		{
			if (base.InvokeRequired)
			{
				Invoke((Action)delegate
				{
					ApplyInputRepeat_MasterVolume(btnFlag);
				});
			}
			else
			{
				ApplyInputRepeat_MasterVolume(btnFlag);
			}
		}
		catch
		{
		}
	}

	private void ApplyInputRepeat_MasterVolume(GamepadButtonFlags btnFlag)
	{
		float masterVolumeRate = MasterVolumeRate;
		if (ActiveController == ControllerCategory.Keyboard)
		{
			return;
		}
		switch (btnFlag)
		{
		case GamepadButtonFlags.DPadRight:
			MasterVolumeRate += MasterVolumeRateAccel;
			if (MasterVolumeRate > MasterVolumeRateMax)
			{
				MasterVolumeRate = MasterVolumeRateMax;
			}
			if (MasterVolumeRate >= 1f)
			{
				MasterVolume += MasterVolumeInput.Floor(MasterVolumeRate);
				if (MasterVolume > (float)volTrackBar.Maximum)
				{
					MasterVolume = volTrackBar.Maximum;
				}
				volTrackBar.Value = (int)MasterVolume;
				volNumLbl.Text = volTrackBar.Value.ToString();
			}
			ActiveController = ControllerCategory.Joypad;
			break;
		case GamepadButtonFlags.DPadLeft:
			MasterVolumeRate += MasterVolumeRateAccel;
			if (MasterVolumeRate > MasterVolumeRateMax)
			{
				MasterVolumeRate = MasterVolumeRateMax;
			}
			if (MasterVolumeRate >= 1f)
			{
				MasterVolume -= MasterVolumeInput.Floor(MasterVolumeRate);
				if (MasterVolume < (float)volTrackBar.Minimum)
				{
					MasterVolume = volTrackBar.Minimum;
				}
				volTrackBar.Value = (int)MasterVolume;
				volNumLbl.Text = volTrackBar.Value.ToString();
			}
			ActiveController = ControllerCategory.Joypad;
			break;
		default:
			MasterVolumeRate *= 0.8f;
			if (MasterVolumeRate < 0.15f)
			{
				MasterVolumeRate = 0f;
			}
			ActiveController = ControllerCategory.None;
			break;
		}
		if (m_Launcher != null && oldMasterVolume != MasterVolume)
		{
			oldMasterVolume = MasterVolume;
			m_Launcher.PlaySound(SoundManager.SoundType.Cursor, MasterVolume / 100f);
		}
	}

	private void Invoke_ApplyInputEvent_UpDown(int offset, bool playSE = true)
	{
		try
		{
			if (base.InvokeRequired)
			{
				Invoke((Action)delegate
				{
					Invoke_ApplyInputEvent_UpDown(offset);
				});
			}
			else
			{
				ApplyInputEvent_UpDown(offset, playSE);
			}
		}
		catch
		{
		}
	}

	private void ApplyInputEvent_UpDown(int offset, bool playSE = true)
	{
		if (offset < 0)
		{
		}
		if (offset < 0)
		{
			if (--controlIndex < 0)
			{
				if (applyBtn.Enabled)
				{
					controlIndex = 7;
				}
				else
				{
					controlIndex = 6;
				}
			}
		}
		else if (offset > 0)
		{
			controlIndex++;
			if (applyBtn.Enabled)
			{
				if (controlIndex > 7)
				{
					controlIndex = 0;
				}
			}
			else if (controlIndex > 6)
			{
				controlIndex = 0;
			}
		}
		ControlList[controlIndex].Focus();
	}

	private void Invoke_ApplyInputEvent_Right()
	{
		try
		{
			if (base.InvokeRequired)
			{
				Invoke((Action)delegate
				{
					Invoke_ApplyInputEvent_Right();
				});
			}
			else
			{
				ApplyInputEvent_Right();
			}
		}
		catch
		{
		}
	}

	private void ApplyInputEvent_Right()
	{
		switch (controlIndex)
		{
		case 0:
			SelectItem(languageBox, 1);
			break;
		case 1:
			ChangeTrackBarValue(1);
			break;
		case 2:
			SelectItem(resolutionBox, 1);
			break;
		case 3:
			SelectItem(screenModeBox, 1);
			break;
		case 4:
			SelectItem(DisplayApplicableBox.Box, 1);
			break;
		}
	}

	private void Invoke_ApplyInputEvent_Left()
	{
		try
		{
			if (base.InvokeRequired)
			{
				Invoke((Action)delegate
				{
					Invoke_ApplyInputEvent_Left();
				});
			}
			else
			{
				ApplyInputEvent_Left();
			}
		}
		catch
		{
		}
	}

	private void ApplyInputEvent_Left()
	{
		switch (controlIndex)
		{
		case 0:
			SelectItem(languageBox, -1);
			break;
		case 1:
			ChangeTrackBarValue(-1);
			break;
		case 2:
			SelectItem(resolutionBox, -1);
			break;
		case 3:
			SelectItem(screenModeBox, -1);
			break;
		case 4:
			SelectItem(DisplayApplicableBox.Box, -1);
			break;
		}
	}

	private void Invoke_ApplyInputEvent_Select()
	{
		try
		{
			if (base.InvokeRequired)
			{
				Invoke((Action)delegate
				{
					Invoke_ApplyInputEvent_Select();
				});
			}
			else
			{
				ApplyInputEvent_Select();
			}
		}
		catch
		{
		}
	}

	private void ApplyInputEvent_Select()
	{
		switch (controlIndex)
		{
		case 0:
			OnSelectButtonPressed(languageBox);
			break;
		case 2:
			OnSelectButtonPressed(resolutionBox);
			break;
		case 3:
			OnSelectButtonPressed(screenModeBox);
			break;
		case 4:
			OnSelectButtonPressed(DisplayApplicableBox.Box);
			break;
		case 5:
			okBtn_Click(this, null);
			break;
		case 6:
			cancelBtn_Click(this, null);
			break;
		case 7:
			applyBtn_Click(this, null);
			break;
		case 1:
			break;
		}
	}

	private void OnSelectButtonPressed(ComboBox cbox)
	{
		if (!cbox.DroppedDown)
		{
			cbox.DroppedDown = true;
			return;
		}
		int selectedIndex = cbox.SelectedIndex;
		cbox.DroppedDown = false;
		cbox.SelectedIndex = selectedIndex;
	}

	private void Invoke_ApplyInputEvent_Cancel()
	{
		try
		{
			if (base.InvokeRequired)
			{
				Invoke((Action)delegate
				{
					Invoke_ApplyInputEvent_Cancel();
				});
			}
			else
			{
				ApplyInputEvent_Cancel();
			}
		}
		catch
		{
		}
	}

	private bool CloseCombobox(ComboBox comboBox)
	{
		if (comboBox.DroppedDown)
		{
			comboBox.DroppedDown = false;
			return true;
		}
		return false;
	}

	private void ApplyInputEvent_Cancel()
	{
		ComboBox comboBox = null;
		switch (controlIndex)
		{
		case 0:
			comboBox = languageBox;
			break;
		case 2:
			comboBox = resolutionBox;
			break;
		case 3:
			comboBox = screenModeBox;
			break;
		case 4:
			comboBox = DisplayApplicableBox.Box;
			break;
		}
		if (comboBox == null || !CloseCombobox(comboBox))
		{
			cancelBtn_Click(this, null);
		}
	}

	private void SelectItem(ComboBox cbox, int offset)
	{
		if (!cbox.DroppedDown)
		{
			cbox.DroppedDown = true;
		}
		switch (offset)
		{
		case -1:
			if (cbox.SelectedIndex > 0)
			{
				cbox.SelectedIndex--;
			}
			break;
		case 1:
			if (cbox.SelectedIndex < cbox.Items.Count - 1)
			{
				cbox.SelectedIndex++;
			}
			break;
		}
	}

	private static void ChangeFocusedComboBoxSelection(ComboBox cbox, int offset)
	{
		if (offset < 0 && cbox.SelectedIndex > 0)
		{
			cbox.SelectedIndex--;
		}
		else if (offset > 0 && cbox.SelectedIndex < cbox.Items.Count - 1)
		{
			cbox.SelectedIndex++;
		}
	}

	private void ChangeTrackBarValue(int offset)
	{
		if (ActiveController == ControllerCategory.Keyboard)
		{
			return;
		}
		switch (offset)
		{
		case -1:
			if (volTrackBar.Value > 0)
			{
				volTrackBar.Value--;
				volNumLbl.Text = volTrackBar.Value.ToString();
			}
			break;
		case 1:
			if (volTrackBar.Value < volTrackBar.Maximum)
			{
				volTrackBar.Value++;
				volNumLbl.Text = volTrackBar.Value.ToString();
			}
			break;
		}
	}

	private void languageBox_Enter(object sender, EventArgs e)
	{
		controlIndex = 0;
	}

	private void volTrackBar_Enter(object sender, EventArgs e)
	{
		controlIndex = 1;
	}

	private void resolutionBox_Enter(object sender, EventArgs e)
	{
		controlIndex = 2;
	}

	private void screenModeBox_Enter(object sender, EventArgs e)
	{
		controlIndex = 3;
	}

	private void DisplayComboBox_Enter(object sender, EventArgs e)
	{
		controlIndex = 4;
	}

	public void StartXInputLoop()
	{
		Running = true;
		new Thread(XInputLoop).Start();
	}

	private void XInputLoop()
	{
		try
		{
			while (Running)
			{
				if (IsActive)
				{
					inputManager.Update(0.01666f);
					if (!ApplyInput())
					{
						ApplyInputRepeat();
					}
				}
				Thread.Sleep(16);
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show(ex.ToString(), Program.Title);
		}
		Console.WriteLine("XInputLoop end");
	}

	private void Setting_FormClosed(object sender, FormClosedEventArgs e)
	{
		AccessibilityReady = false;
		Running = false;
		volTrackBar.KeyDown -= VolTrackBar_KeyDown;
		volTrackBar.KeyUp -= VolTrackBar_KeyUp;
	}

	public void StartDInputLoop(DInput dinput)
	{
		if (dinput != null)
		{
			this.dinput = dinput;
			Running = true;
			new Thread(DInputLoop).Start();
		}
	}

	private void DInputLoop()
	{
		try
		{
			while (Running)
			{
				if (IsActive && !ApplyDInput())
				{
					ApplyDInputRepeat();
				}
				Thread.Sleep(16);
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show(ex.ToString(), Program.Title);
		}
		Console.WriteLine("DInputLoop end");
	}

	public bool ApplyDInput()
	{
		if (m_Launcher == null)
		{
			return false;
		}
		if (dinput == null)
		{
			return false;
		}
		dinput.Update(0.01666f);
		bool result = false;
		if (dinput.ControllerButtonJustPressed(DInput.DINPUT_BUTTON.DIB_UP))
		{
			if (Invoke_CheckComboBoxDroppedDown())
			{
				Invoke_ApplyInputEvent_Left();
			}
			else
			{
				Invoke_ApplyInputEvent_UpDown(-1);
			}
			result = true;
		}
		else if (dinput.ControllerButtonJustPressed(DInput.DINPUT_BUTTON.DIB_DOWN))
		{
			if (Invoke_CheckComboBoxDroppedDown())
			{
				Invoke_ApplyInputEvent_Right();
			}
			else
			{
				Invoke_ApplyInputEvent_UpDown(1);
			}
			result = true;
		}
		else if (dinput.ControllerButtonJustPressed(DInput.DINPUT_BUTTON.DIB_RIGHT))
		{
			switch (controlIndex)
			{
			case 5:
				Invoke_ApplyInputEvent_UpDown(1);
				break;
			case 6:
				if (applyBtn.Enabled)
				{
					Invoke_ApplyInputEvent_UpDown(1);
				}
				else
				{
					Invoke_ApplyInputEvent_UpDown(-1);
				}
				break;
			case 7:
				Invoke_ApplyInputEvent_UpDown(-1);
				Invoke_ApplyInputEvent_UpDown(-1, playSE: false);
				break;
			default:
				Invoke_ApplyInputEvent_Right();
				break;
			}
			result = true;
		}
		else if (dinput.ControllerButtonJustPressed(DInput.DINPUT_BUTTON.DIB_LEFT))
		{
			switch (controlIndex)
			{
			case 5:
				if (applyBtn.Enabled)
				{
					Invoke_ApplyInputEvent_UpDown(1);
					Invoke_ApplyInputEvent_UpDown(1, playSE: false);
				}
				else
				{
					Invoke_ApplyInputEvent_UpDown(1);
				}
				break;
			case 6:
			case 7:
				Invoke_ApplyInputEvent_UpDown(-1);
				break;
			default:
				Invoke_ApplyInputEvent_Left();
				break;
			}
			result = true;
		}
		else if (ProcessDXInput())
		{
			result = true;
		}
		return result;
	}

	public bool ProcessDXInput()
	{
		bool result = false;
		if (DXInput == null)
		{
			Trace.WriteLine("DXInput null");
			return false;
		}
		if (DXInput.ControllerButtonJustPressed(JoyKey.Select))
		{
			Invoke_ApplyInputEvent_Select();
			result = true;
		}
		else if (DXInput.ControllerButtonJustPressed(JoyKey.Cansel))
		{
			Invoke_ApplyInputEvent_Cancel();
			result = true;
		}
		return result;
	}

	public void ApplyDInputRepeat()
	{
		GamepadButtonFlags btnFlag = GamepadButtonFlags.None;
		int fwd = 0;
		if (dinput.ControllerButtonPressed(DInput.DINPUT_BUTTON.DIB_UP))
		{
			btnFlag = GamepadButtonFlags.DPadUp;
			fwd = -1;
		}
		else if (dinput.ControllerButtonPressed(DInput.DINPUT_BUTTON.DIB_DOWN))
		{
			btnFlag = GamepadButtonFlags.DPadDown;
			fwd = 1;
		}
		if (dinput.ControllerButtonPressed(DInput.DINPUT_BUTTON.DIB_RIGHT))
		{
			btnFlag = GamepadButtonFlags.DPadRight;
			fwd = 1;
		}
		else if (dinput.ControllerButtonPressed(DInput.DINPUT_BUTTON.DIB_LEFT))
		{
			btnFlag = GamepadButtonFlags.DPadLeft;
			fwd = -1;
		}
		if (controlIndex == 1)
		{
			Invoke_MasterVolume(btnFlag);
			return;
		}
		int num = controlIndex;
		int num2 = num;
		if (num2 == 0 || (uint)(num2 - 2) <= 2u)
		{
			Invoke_ComboBoxKeyRepeat(btnFlag, fwd);
		}
	}

	private void Invoke_ComboBoxKeyRepeat(GamepadButtonFlags btnFlag, int fwd)
	{
		try
		{
			if (base.InvokeRequired)
			{
				Invoke((Action)delegate
				{
					ApplyInputRepeat_ComboBoxKeyRepeat(btnFlag, fwd);
				});
			}
			else
			{
				ApplyInputRepeat_ComboBoxKeyRepeat(btnFlag, fwd);
			}
		}
		catch
		{
		}
	}

	private void ApplyInputRepeat_ComboBoxKeyRepeat(GamepadButtonFlags btnFlag, int fwd)
	{
		switch (fwd)
		{
		case 1:
			MasterVolumeRate += MasterVolumeRateAccel;
			if (MasterVolumeRate > MasterVolumeRateMax)
			{
				MasterVolumeRate = MasterVolumeRateMax;
			}
			if (MasterVolumeRate >= 1f && --waitComboBoxKeyRepeat == 0)
			{
				waitComboBoxKeyRepeat = 4;
				Invoke_ApplyInputEvent_Right();
			}
			break;
		case -1:
			MasterVolumeRate += MasterVolumeRateAccel;
			if (MasterVolumeRate > MasterVolumeRateMax)
			{
				MasterVolumeRate = MasterVolumeRateMax;
			}
			if (MasterVolumeRate >= 1f && --waitComboBoxKeyRepeat == 0)
			{
				waitComboBoxKeyRepeat = 4;
				Invoke_ApplyInputEvent_Left();
			}
			break;
		default:
			MasterVolumeRate *= 0.8f;
			if (MasterVolumeRate < 0.15f)
			{
				MasterVolumeRate = 0f;
			}
			break;
		}
	}

	private void DisplayComboBox_SelectedIndexChanged(object sender, EventArgs e)
	{
		if (DisplayApplicableBox != null && DisplayApplicableBox.SelectIndex(DisplayApplicableBox.SelectedIndex))
		{
			applyBtn.Enabled = true;
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && components != null)
		{
			components.Dispose();
		}
		base.Dispose(disposing);
	}

	private void InitializeComponent()
	{
		System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FF7_Launcher.Setting));
		this.languageBox = new System.Windows.Forms.ComboBox();
		this.languageLbl = new System.Windows.Forms.Label();
		this.textLbl = new System.Windows.Forms.Label();
		this.audioLbl = new System.Windows.Forms.Label();
		this.masterVolumeLbl = new System.Windows.Forms.Label();
		this.volMinusLbl = new System.Windows.Forms.Label();
		this.volTrackBar = new FF7_Launcher.AccessibleSettingsTrackBar();
		this.volPlusLbl = new System.Windows.Forms.Label();
		this.volNumLbl = new System.Windows.Forms.Label();
		this.graphicLbl = new System.Windows.Forms.Label();
		this.resolutionLbl = new System.Windows.Forms.Label();
		this.resolutionBox = new System.Windows.Forms.ComboBox();
		this.okBtn = new FF7_Launcher.AccessibleSettingsButton();
		this.cancelBtn = new FF7_Launcher.AccessibleSettingsButton();
		this.applyBtn = new FF7_Launcher.AccessibleSettingsButton();
		this.screenModeBox = new System.Windows.Forms.ComboBox();
		this.screenModeLbl = new System.Windows.Forms.Label();
		this.VolIcon = new System.Windows.Forms.PictureBox();
		this.DisplayLbl = new System.Windows.Forms.Label();
		this.DisplayComboBox = new System.Windows.Forms.ComboBox();
		((System.ComponentModel.ISupportInitialize)this.volTrackBar).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.VolIcon).BeginInit();
		base.SuspendLayout();
		this.languageBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.languageBox.FormattingEnabled = true;
		resources.ApplyResources(this.languageBox, "languageBox");
		this.languageBox.Name = "languageBox";
		this.languageBox.SelectedIndexChanged += new System.EventHandler(languageBox_SelectedIndexChanged);
		this.languageBox.Enter += new System.EventHandler(languageBox_Enter);
		resources.ApplyResources(this.languageLbl, "languageLbl");
		this.languageLbl.Name = "languageLbl";
		resources.ApplyResources(this.textLbl, "textLbl");
		this.textLbl.Name = "textLbl";
		resources.ApplyResources(this.audioLbl, "audioLbl");
		this.audioLbl.Name = "audioLbl";
		resources.ApplyResources(this.masterVolumeLbl, "masterVolumeLbl");
		this.masterVolumeLbl.Name = "masterVolumeLbl";
		resources.ApplyResources(this.volMinusLbl, "volMinusLbl");
		this.volMinusLbl.Name = "volMinusLbl";
		resources.ApplyResources(this.volTrackBar, "volTrackBar");
		this.volTrackBar.Name = "volTrackBar";
		this.volTrackBar.TickStyle = System.Windows.Forms.TickStyle.Both;
		this.volTrackBar.ValueChanged += new System.EventHandler(volTrackBar_ValueChanged);
		this.volTrackBar.Enter += new System.EventHandler(volTrackBar_Enter);
		resources.ApplyResources(this.volPlusLbl, "volPlusLbl");
		this.volPlusLbl.Name = "volPlusLbl";
		this.volNumLbl.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
		resources.ApplyResources(this.volNumLbl, "volNumLbl");
		this.volNumLbl.Name = "volNumLbl";
		resources.ApplyResources(this.graphicLbl, "graphicLbl");
		this.graphicLbl.Name = "graphicLbl";
		resources.ApplyResources(this.resolutionLbl, "resolutionLbl");
		this.resolutionLbl.Name = "resolutionLbl";
		this.resolutionBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.resolutionBox.FormattingEnabled = true;
		resources.ApplyResources(this.resolutionBox, "resolutionBox");
		this.resolutionBox.Name = "resolutionBox";
		this.resolutionBox.SelectedIndexChanged += new System.EventHandler(resolutionBox_SelectedIndexChanged);
		this.resolutionBox.Enter += new System.EventHandler(resolutionBox_Enter);
		resources.ApplyResources(this.okBtn, "okBtn");
		this.okBtn.Name = "okBtn";
		this.okBtn.UseVisualStyleBackColor = true;
		this.okBtn.Click += new System.EventHandler(okBtn_Click);
		resources.ApplyResources(this.cancelBtn, "cancelBtn");
		this.cancelBtn.Name = "cancelBtn";
		this.cancelBtn.UseVisualStyleBackColor = true;
		this.cancelBtn.Click += new System.EventHandler(cancelBtn_Click);
		resources.ApplyResources(this.applyBtn, "applyBtn");
		this.applyBtn.Name = "applyBtn";
		this.applyBtn.UseVisualStyleBackColor = true;
		this.applyBtn.Click += new System.EventHandler(applyBtn_Click);
		this.screenModeBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.screenModeBox.FormattingEnabled = true;
		resources.ApplyResources(this.screenModeBox, "screenModeBox");
		this.screenModeBox.Name = "screenModeBox";
		this.screenModeBox.SelectedIndexChanged += new System.EventHandler(screenModeBox_SelectedIndexChanged);
		this.screenModeBox.Enter += new System.EventHandler(screenModeBox_Enter);
		resources.ApplyResources(this.screenModeLbl, "screenModeLbl");
		this.screenModeLbl.Name = "screenModeLbl";
		this.VolIcon.Image = FF7_Launcher.Properties.Resources.UI_HUD_Launcher_Icon_Sound;
		resources.ApplyResources(this.VolIcon, "VolIcon");
		this.VolIcon.Name = "VolIcon";
		this.VolIcon.TabStop = false;
		this.VolIcon.Click += new System.EventHandler(volIcon_Click);
		resources.ApplyResources(this.DisplayLbl, "DisplayLbl");
		this.DisplayLbl.Name = "DisplayLbl";
		this.DisplayComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
		this.DisplayComboBox.FormattingEnabled = true;
		resources.ApplyResources(this.DisplayComboBox, "DisplayComboBox");
		this.DisplayComboBox.Name = "DisplayComboBox";
		this.DisplayComboBox.SelectedIndexChanged += new System.EventHandler(DisplayComboBox_SelectedIndexChanged);
		this.DisplayComboBox.Enter += new System.EventHandler(DisplayComboBox_Enter);
		resources.ApplyResources(this, "$this");
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
		base.Controls.Add(this.DisplayComboBox);
		base.Controls.Add(this.DisplayLbl);
		base.Controls.Add(this.VolIcon);
		base.Controls.Add(this.screenModeBox);
		base.Controls.Add(this.screenModeLbl);
		base.Controls.Add(this.applyBtn);
		base.Controls.Add(this.cancelBtn);
		base.Controls.Add(this.okBtn);
		base.Controls.Add(this.resolutionBox);
		base.Controls.Add(this.resolutionLbl);
		base.Controls.Add(this.graphicLbl);
		base.Controls.Add(this.volNumLbl);
		base.Controls.Add(this.volPlusLbl);
		base.Controls.Add(this.volTrackBar);
		base.Controls.Add(this.volMinusLbl);
		base.Controls.Add(this.masterVolumeLbl);
		base.Controls.Add(this.audioLbl);
		base.Controls.Add(this.textLbl);
		base.Controls.Add(this.languageLbl);
		base.Controls.Add(this.languageBox);
		base.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
		base.MaximizeBox = false;
		base.Name = "Setting";
		base.Activated += new System.EventHandler(Setting_Activated);
		base.FormClosed += new System.Windows.Forms.FormClosedEventHandler(Setting_FormClosed);
		((System.ComponentModel.ISupportInitialize)this.volTrackBar).EndInit();
		((System.ComponentModel.ISupportInitialize)this.VolIcon).EndInit();
		base.ResumeLayout(false);
		base.PerformLayout();
	}
}
