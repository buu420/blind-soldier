using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Resources;
using System.Runtime.CompilerServices;

namespace FF7_Launcher.Properties;

[GeneratedCode("System.Resources.Tools.StronglyTypedResourceBuilder", "17.0.0.0")]
[DebuggerNonUserCode]
[CompilerGenerated]
internal class Resources
{
	private static ResourceManager resourceMan;

	private static CultureInfo resourceCulture;

	[EditorBrowsable(EditorBrowsableState.Advanced)]
	internal static ResourceManager ResourceManager
	{
		get
		{
			if (resourceMan == null)
			{
				ResourceManager resourceManager = new ResourceManager("FF7_Launcher.Properties.Resources", typeof(Resources).Assembly);
				resourceMan = resourceManager;
			}
			return resourceMan;
		}
	}

	[EditorBrowsable(EditorBrowsableState.Advanced)]
	internal static CultureInfo Culture
	{
		get
		{
			return resourceCulture;
		}
		set
		{
			resourceCulture = value;
		}
	}

	internal static UnmanagedMemoryStream cancel => ResourceManager.GetStream("cancel", resourceCulture);

	internal static UnmanagedMemoryStream cursor => ResourceManager.GetStream("cursor", resourceCulture);

	internal static UnmanagedMemoryStream decide => ResourceManager.GetStream("decide", resourceCulture);

	internal static Icon FF7
	{
		get
		{
			object obj = ResourceManager.GetObject("FF7", resourceCulture);
			return (Icon)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BG
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BG", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BG_EFIGS
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BG_EFIGS", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_exit_00_DE
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_exit_00_DE", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_exit_00_EN
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_exit_00_EN", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_exit_00_ES
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_exit_00_ES", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_exit_00_FR
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_exit_00_FR", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_exit_00_JP
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_exit_00_JP", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_exit_01_DE
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_exit_01_DE", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_exit_01_EN
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_exit_01_EN", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_exit_01_ES
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_exit_01_ES", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_exit_01_FR
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_exit_01_FR", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_exit_01_JP
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_exit_01_JP", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_exit_02_DE
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_exit_02_DE", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_exit_02_EN
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_exit_02_EN", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_exit_02_ES
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_exit_02_ES", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_exit_02_FR
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_exit_02_FR", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_exit_02_JP
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_exit_02_JP", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_play_00
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_play_00", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_play_00_DE
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_play_00_DE", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Play_00_EN
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Play_00_EN", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_play_00_ES
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_play_00_ES", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_play_00_FR
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_play_00_FR", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Play_00_JP
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Play_00_JP", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Play_01
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Play_01", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Play_01_DE
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Play_01_DE", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Play_01_EN
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Play_01_EN", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Play_01_ES
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Play_01_ES", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Play_01_FR
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Play_01_FR", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Play_01_JP
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Play_01_JP", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Play_02
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Play_02", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Play_02_DE
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Play_02_DE", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Play_02_EN
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Play_02_EN", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Play_02_ES
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Play_02_ES", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Play_02_FR
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Play_02_FR", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Play_02_JP
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Play_02_JP", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Setting_00
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Setting_00", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Setting_00_DE
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Setting_00_DE", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Setting_00_EN
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Setting_00_EN", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Setting_00_ES
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Setting_00_ES", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Setting_00_FR
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Setting_00_FR", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Setting_00_JP
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Setting_00_JP", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Setting_01
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Setting_01", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Setting_01_DE
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Setting_01_DE", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Setting_01_EN
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Setting_01_EN", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Setting_01_ES
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Setting_01_ES", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Setting_01_FR
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Setting_01_FR", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Setting_01_JP
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Setting_01_JP", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Setting_02
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Setting_02", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Setting_02_DE
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Setting_02_DE", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Setting_02_EN
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Setting_02_EN", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Setting_02_ES
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Setting_02_ES", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Setting_02_FR
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Setting_02_FR", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_BTN_Setting_02_JP
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_BTN_Setting_02_JP", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal static Bitmap UI_HUD_Launcher_Icon_Sound
	{
		get
		{
			object obj = ResourceManager.GetObject("UI_HUD_Launcher_Icon_Sound", resourceCulture);
			return (Bitmap)obj;
		}
	}

	internal Resources()
	{
	}
}
