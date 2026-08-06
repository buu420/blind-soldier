using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace FF7_Launcher;

public static class RuntimeLocalizer
{
	public static event EventHandler<ChangeCultureEventArgs> OnChangeCulture;

	public static void ChangeCulture(Form frm, string cultureCode)
	{
		CultureInfo cultureInfo = CultureInfo.GetCultureInfo(cultureCode);
		Thread.CurrentThread.CurrentUICulture = cultureInfo;
		ComponentResourceManager resources = new ComponentResourceManager(frm.GetType());
		ApplyResourceToControlFromRes(resources, frm, cultureInfo);
		RuntimeLocalizer.OnChangeCulture(frm, new ChangeCultureEventArgs(cultureCode));
	}

	private static void ApplyResourceToControlFromRes(ComponentResourceManager resources, Control control, CultureInfo lang)
	{
		foreach (Control control2 in control.Controls)
		{
			ApplyResourceToControlFromRes(resources, control2, lang);
			string text = resources.GetString(control2.Name + ".Text", lang);
			if (text != null)
			{
				control2.Text = text;
			}
		}
	}

	private static void ApplyResourceToControl(ComponentResourceManager res, Control control, CultureInfo lang)
	{
		if (control.GetType() == typeof(MenuStrip))
		{
			MenuStrip menuStrip = (MenuStrip)control;
			ApplyResourceToToolStripItemCollection(menuStrip.Items, res, lang);
		}
		foreach (Control control2 in control.Controls)
		{
			ApplyResourceToControl(res, control2, lang);
			res.ApplyResources(control2, control2.Name, lang);
		}
		res.ApplyResources(control, control.Name, lang);
	}

	private static void ApplyResourceToToolStripItemCollection(ToolStripItemCollection col, ComponentResourceManager res, CultureInfo lang)
	{
		for (int i = 0; i < col.Count; i++)
		{
			ToolStripItem toolStripItem = (ToolStripMenuItem)col[i];
			if (toolStripItem.GetType() == typeof(ToolStripMenuItem))
			{
				ToolStripMenuItem toolStripMenuItem = (ToolStripMenuItem)toolStripItem;
				ApplyResourceToToolStripItemCollection(toolStripMenuItem.DropDownItems, res, lang);
			}
			res.ApplyResources(toolStripItem, toolStripItem.Name, lang);
		}
	}
}
