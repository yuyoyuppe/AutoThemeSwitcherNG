using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing.Design;
using System.Windows.Forms;
using System.Windows.Forms.Design;

namespace AutoThemeSwitcherNG;

internal sealed class ThemeIdEditor : UITypeEditor {
    public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) => UITypeEditorEditStyle.DropDown;

    public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider provider, object? value) {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (provider.GetService(typeof(IWindowsFormsEditorService)) is not IWindowsFormsEditorService edSvc) {
            return value;
        }

        Guid current = value is Guid g ? g : Guid.Empty;
        ListBox list = new() {
            BorderStyle = BorderStyle.None,
            IntegralHeight = true,
        };

        IReadOnlyList<ThemeManager.ThemeInfo> themes = ThemeManager.GetAvailableThemes();
        foreach (ThemeManager.ThemeInfo t in themes) {
            int idx = list.Items.Add(new ThemeListItem(t.Id, t.Name));
            if (t.Id == current) {
                list.SelectedIndex = idx;
            }
        }

        list.Click += (_, _) => edSvc.CloseDropDown();
        edSvc.DropDownControl(list);

        return list.SelectedItem is ThemeListItem selected ? selected.Id : value;
    }

    private sealed class ThemeListItem {
        public ThemeListItem(Guid id, string name) {
            Id = id;
            Name = name;
        }

        public Guid Id { get; }
        public string Name { get; }

        public override string ToString() => Name;
    }
}

