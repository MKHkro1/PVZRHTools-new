using System.Collections;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using ToolModData;

namespace PVZRHTools;

public class SelectedItemsExt : DependencyObject
{
    // Using a DependencyProperty as the backing store for SelectedItems.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty SelectedItemsProperty =
        DependencyProperty.RegisterAttached("SelectedItems", typeof(IList), typeof(SelectedItemsExt),
            new PropertyMetadata(OnSelectedItemsChanged));

    public static IList GetSelectedItems(DependencyObject obj)
    {
        return (IList)obj.GetValue(SelectedItemsProperty);
    }

    public static void OnlistBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var dataSource = GetSelectedItems((sender as DependencyObject)!);
        foreach (var item in e.AddedItems) dataSource.Add(item);
        foreach (var item in e.RemovedItems) dataSource.Remove(item);
        SetSelectedItems((sender as DependencyObject)!, dataSource);
    }

    public static void OnSelectedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ListBox listBox && listBox.SelectionMode is SelectionMode.Multiple)
        {
            if (e.OldValue is not null) listBox.SelectionChanged -= OnlistBoxSelectionChanged;
            var collection = (e.NewValue as IList)!;
            listBox.SelectedItems.Clear();
            if (collection is not null)
            {
                foreach (var item in collection) listBox.SelectedItems.Add(item);
                listBox.OnApplyTemplate();
                listBox.SelectionChanged += OnlistBoxSelectionChanged;
            }
        }
    }

    public static void SetSelectedItems(DependencyObject obj, IList value)
    {
        obj.SetValue(SelectedItemsProperty, value);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(BasicProperties))]
internal partial class BasicPropertiesSGC : JsonSerializerContext

{
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Exit))]
internal partial class ExitSGC : JsonSerializerContext

{
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(GameModes))]
internal partial class GameModesSGC : JsonSerializerContext

{
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(InGameActions))]
internal partial class InGameActionsSGC : JsonSerializerContext

{
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(InGameHotkeys))]
internal partial class InGameHotkeysSGC : JsonSerializerContext

{
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(InitData))]
internal partial class InitDataSGC : JsonSerializerContext

{
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ISyncData))]
internal partial class ISyncDataSGC : JsonSerializerContext

{
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ModifierSaveModel))]
internal partial class ModifierSaveModelSGC : JsonSerializerContext

{
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SyncAll))]
internal partial class SyncAllSGC : JsonSerializerContext

{
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SyncTravelBuff))]
internal partial class SyncTravelBuffSGC : JsonSerializerContext

{
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ValueProperties))]
internal partial class ValuePropertiesSGC : JsonSerializerContext

{
}