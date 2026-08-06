using System.Windows.Forms;

namespace FF7_Launcher;

public class ApplicableCombobox
{
	private int OldIndex = -1;

	private ComboBox _ComboBox;

	public ComboBox Box => _ComboBox;

	public int SelectedIndex => _ComboBox.SelectedIndex;

	public bool DroppedDown => _ComboBox.DroppedDown;

	public ApplicableCombobox(ComboBox comboBox, object[] items, int initIndex)
	{
		comboBox.Items.AddRange(items);
		_ComboBox = comboBox;
		_ComboBox.SelectedIndex = initIndex;
		OldIndex = _ComboBox.SelectedIndex;
	}

	public bool SelectIndex(int index)
	{
		if (OldIndex == index)
		{
			return false;
		}
		OldIndex = index;
		return true;
	}

	public void Select()
	{
		_ComboBox.Select();
	}

	public void Apply()
	{
		OldIndex = _ComboBox.SelectedIndex;
	}
}
