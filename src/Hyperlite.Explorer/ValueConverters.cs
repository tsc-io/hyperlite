namespace Hyperlite.Explorer
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using System.Threading.Tasks;
    using System.Windows.Data;

    [ValueConversion(typeof(Key), typeof(string))]
    internal sealed class TimestampToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (targetType == null)
            {
                throw new ArgumentNullException(nameof(targetType));
            }

            var key = (Key)value;
            if (targetType.IsAssignableFrom(typeof(string)))
            {
                return key.DateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            }

            throw new NotImplementedException();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    [ValueConversion(typeof(CellInfo), typeof(string))]
    internal sealed class CellInfoValueToStringConverter : IValueConverter
    {
        public static char ToChar(byte b)
        {
            return b > 31 && !(b > 126 && b < 176) ? (char)b : '.';
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (targetType == null)
            {
                throw new ArgumentNullException(nameof(targetType));
            }

            var cellInfo = (CellInfo)value;
            if (targetType.IsAssignableFrom(typeof(string)))
            {
                var sb = new StringBuilder(CellInfo.Limit + 4);
                if (cellInfo.ValueInfo != null)
                {
                    foreach (var b in cellInfo.ValueInfo)
                    {
                        sb.Append(ToChar(b));
                    }

                    if (cellInfo.CellValueSize > CellInfo.Limit)
                    {
                        sb.Append(" (*)");
                    }
                }

                return sb.ToString();
            }

            throw new NotImplementedException();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    internal struct SixteenBytes
    {
        public const int Size = 16;

        public int Index;

        public byte[] Value;
    }

    [ValueConversion(typeof(byte[]), typeof(IEnumerable))]
    internal sealed class BytesToSixteenBytesConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (targetType == null)
            {
                throw new ArgumentNullException(nameof(targetType));
            }

            var bytes = (byte[])value;
            if (targetType.IsAssignableFrom(typeof(IEnumerable)))
            {
                var observable = new ObservableList<SixteenBytes>();
                if (bytes != null && bytes.Length > 0)
                {
                    Task.Factory.StartNew(
                        () =>
                            {
                                const int ChunkSize = 16;
                                var chunk = new List<SixteenBytes>(ChunkSize);
                                int n;
                                var len = bytes.Length;
                                for (n = 0; n < len; n += SixteenBytes.Size)
                                {
                                    chunk.Add(new SixteenBytes { Value = bytes, Index = n });
                                    if (chunk.Count == chunk.Capacity)
                                    {
                                        observable.AddRange(chunk);
                                        chunk = new List<SixteenBytes>(ChunkSize);
                                    }
                                }

                                if (n < len)
                                {
                                    chunk.Add(new SixteenBytes { Value = bytes, Index = n });
                                }

                                if (chunk.Count > 0)
                                {
                                    observable.AddRange(chunk);
                                }
                            });
                }

                return observable;
            }

            throw new NotImplementedException();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// The sixteen bytes to address converter.
    /// </summary>
    [ValueConversion(typeof(SixteenBytes), typeof(string))]
    internal sealed class SixteenBytesToAddrConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (targetType == null)
            {
                throw new ArgumentNullException(nameof(targetType));
            }

            var sixteenBytes = (SixteenBytes)value;
            if (targetType.IsAssignableFrom(typeof(string)))
            {
                return (sixteenBytes.Index / SixteenBytes.Size).ToString("X8", CultureInfo.InvariantCulture);
            }

            throw new NotImplementedException();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    [ValueConversion(typeof(SixteenBytes), typeof(string))]
    internal sealed class SixteenBytesToHexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (targetType == null)
            {
                throw new ArgumentNullException(nameof(targetType));
            }

            var sixteenBytes = (SixteenBytes)value;
            if (targetType.IsAssignableFrom(typeof(string)))
            {
                var sb = new StringBuilder(SixteenBytes.Size * 3);
                for (var n = sixteenBytes.Index; n < Math.Min(sixteenBytes.Value.Length, sixteenBytes.Index + SixteenBytes.Size); ++n)
                {
                    sb.Append(sixteenBytes.Value[n].ToString("X2", CultureInfo.InvariantCulture));
                    sb.Append(" ");
                }

                sb.Remove(sb.Length - 1, 1);
                return sb.ToString();
            }

            throw new NotImplementedException();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    [ValueConversion(typeof(SixteenBytes), typeof(string))]
    internal sealed class SixteenBytesToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (targetType == null)
            {
                throw new ArgumentNullException(nameof(targetType));
            }

            var sixteenBytes = (SixteenBytes)value;
            if (targetType.IsAssignableFrom(typeof(string)))
            {
                var sb = new StringBuilder(SixteenBytes.Size);
                for (var n = sixteenBytes.Index; n < Math.Min(sixteenBytes.Value.Length, sixteenBytes.Index + SixteenBytes.Size); ++n)
                {
                    sb.Append(CellInfoValueToStringConverter.ToChar(sixteenBytes.Value[n]));
                }

                return sb.ToString();
            }

            throw new NotImplementedException();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
