using System.Globalization;
using Avalonia.Data.Converters;
using LizTerm.Core.Session;

namespace LizTerm.App.Views;

/// <summary>Labels for the transfer enums in the dialog's combo boxes: TSO, VM, CICS, AVBLOCK; every other member
/// shows its own name (Default, Fixed, Tracks, ...).</summary>
public sealed class TransferLabels : IValueConverter
{
    public static readonly TransferLabels Converter = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        TransferHostType.Tso => "TSO",
        TransferHostType.Vm => "VM",
        TransferHostType.Cics => "CICS",
        AllocationUnits.AvBlock => "AVBLOCK",
        null => null,
        _ => value.ToString(),
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Labels are display-only.");
}
