using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FF7_Launcher;

[ComVisible(true)]
internal sealed class LauncherAccessibleObject : Control.ControlAccessibleObject
{
    private readonly FF7Launcher owner;
    private readonly LauncherButtonAccessibleObject[] children;

    internal LauncherAccessibleObject(FF7Launcher owner)
        : base(owner)
    {
        this.owner = owner;
        children = new[]
        {
            new LauncherButtonAccessibleObject(owner, this, 0),
            new LauncherButtonAccessibleObject(owner, this, 1),
            new LauncherButtonAccessibleObject(owner, this, 2)
        };
    }

    public override int GetChildCount()
    {
        return children.Length;
    }

    public override AccessibleObject GetChild(int index)
    {
        return index >= 0 && index < children.Length ? children[index] : null;
    }

    public override AccessibleObject GetFocused()
    {
        return children[owner.AccessibleButtonIndex];
    }

    public override AccessibleObject GetSelected()
    {
        return children[owner.AccessibleButtonIndex];
    }

    public override AccessibleObject HitTest(int x, int y)
    {
        var point = new Point(x, y);
        foreach (var child in children)
        {
            if (child.Bounds.Contains(point))
            {
                return child;
            }
        }
        return Bounds.Contains(point) ? this : null;
    }
}
