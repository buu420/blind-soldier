#define TRACE
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace FF7_Launcher;

internal class CustomVisualButton
{
	public enum ButtonState
	{
		Deactive,
		MouseOn,
		Selected
	}

	private Rectangle _Bound;

	private Graphics Renderer;

	private Rectangle OriginalBound;

	private Point Location;

	private bool IsOnMouse;

	private bool PreIsOnMouse;

	public bool Dirty;

	private List<Bitmap> Images;

	private bool IsLock;

	public Rectangle Bound
	{
		get
		{
			return _Bound;
		}
		set
		{
			_Bound = value;
		}
	}

	public ButtonState State { get; private set; }

	public bool HasFocus => State == ButtonState.MouseOn;

	public ButtonType Name { get; set; }

	public event EventHandler<EventArgs> OnClickCommitted;

	public event EventHandler<EventArgs> OnClick;

	public event EventHandler<EventArgs> OnNeedRepaint;

	public event EventHandler<EventArgs> OnMouseHover;

	private void SetState(ButtonState state)
	{
		if (IsLock)
		{
			Trace.WriteLine($"{Name} SetState {state} but locked");
			return;
		}
		Trace.WriteLine(Name.ToString() + state);
		State = state;
		if (this.OnNeedRepaint != null)
		{
			this.OnNeedRepaint(this, null);
		}
	}

	public void AssignImage(List<Bitmap> bitmaps)
	{
		Images = bitmaps;
		Bound = new Rectangle(Location, Images[0].Size);
		Trace.WriteLine("ButtonType : " + Name);
		Trace.WriteLine("AssignImage Bound : " + Bound.ToString());
		OriginalBound = Bound;
	}

	public void Resize(float ratio)
	{
		Bound = OriginalBound.Resize(ratio);
	}

	public static bool IsNearEqual(float v0, float v1)
	{
		return Math.Abs(v0 - v1) < float.Epsilon;
	}

	public CustomVisualButton(Point location, Point MousePos, ButtonType name)
	{
		TickMouse(MouseButtons.None, MousePos);
		Trace.WriteLine("ButtonType : " + name);
		Point point = location;
		Trace.WriteLine("location : " + point.ToString());
		Location = location;
		Name = name;
	}

	public void Lock()
	{
		IsLock = true;
	}

	public void UnLock()
	{
		IsLock = false;
	}

	public void Focus()
	{
		SetState(ButtonState.MouseOn);
	}

	public void Click()
	{
		SetState(ButtonState.Selected);
	}

	public void TickMouse(MouseButtons mouseButtons, Point p)
	{
		IsOnMouse = Bound.Contains(p);
		if (PreIsOnMouse != IsOnMouse)
		{
			ButtonState buttonState = (IsOnMouse ? ButtonState.MouseOn : ButtonState.Deactive);
			if (buttonState == ButtonState.MouseOn && this.OnMouseHover != null)
			{
				this.OnMouseHover(this, null);
			}
			SetState(buttonState);
			Dirty = true;
		}
		PreIsOnMouse = IsOnMouse;
	}

	public void UpdateByMousePosition(MouseButtons mouseButtons, Point p)
	{
		ButtonState state = (Bound.Contains(p) ? ButtonState.MouseOn : ButtonState.Deactive);
		SetState(state);
		Dirty = true;
	}

	public void OnPaint(PaintEventArgs e = null)
	{
		e.Graphics.DrawImage(Images[(int)State], Bound);
		Renderer = e.Graphics;
		Dirty = false;
	}

	public void ForcePaint(Graphics graphics)
	{
		graphics.DrawImage(Images[(int)State], Bound);
	}

	internal void OnClicked(MouseButtons mouseButtons, Point mousePosition)
	{
		if (mouseButtons == MouseButtons.Left && Bound.Contains(mousePosition))
		{
			SetState(ButtonState.Selected);
			if (this.OnClick != null)
			{
				this.OnClick(this, new EventArgs());
			}
			Dirty = true;
		}
	}

	internal void OnMouseUp(MouseButtons mouseButtons, Point mousePosition)
	{
		if (mouseButtons == MouseButtons.Left && Bound.Contains(mousePosition) && State == ButtonState.Selected)
		{
			if (Name == ButtonType.Play)
			{
				SetState(ButtonState.MouseOn);
			}
			if (this.OnClickCommitted != null)
			{
				this.OnClickCommitted(this, new EventArgs());
			}
			if (Name == ButtonType.Setting)
			{
				SetState(ButtonState.MouseOn);
			}
			Dirty = true;
		}
	}

	internal void LostFocus()
	{
		SetState(ButtonState.Deactive);
	}
}
