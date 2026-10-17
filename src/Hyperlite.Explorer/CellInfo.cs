namespace Hyperlite.Explorer
{
    using System;

    internal sealed class CellInfo
    {
        public const int Limit = 64;

        private readonly Cell cell;

        private readonly DatabaseDirectoryInfo directoryInfo;

        private readonly byte[] value;

        public CellInfo(DatabaseDirectoryInfo directoryInfo, Cell cell)
        {
            this.directoryInfo = directoryInfo;
            this.cell = cell;
            if (this.cell.Value != null)
            {
                this.CellValueSize = this.cell.Value.Length;
                if (this.CellValueSize > Limit)
                {
                    this.value = this.cell.Value;
                    Array.Resize(ref this.value, Limit);
                    this.cell.Value = null;
                }
            }
        }

        public int CellValueSize { get; private set; }

        public Key Key
        {
            get
            {
                return this.cell.Key;
            }
        }

        public byte[] Value
        {
            get
            {
                if (this.cell.Value == null && this.CellValueSize > 0)
                {
                    var c = this.directoryInfo.Find(this.cell.Key);
                    if (c != null)
                    {
                        return c.Value;
                    }
                }

                return this.cell.Value;
            }
        }

        public byte[] ValueInfo
        {
            get
            {
                return this.value ?? this.cell.Value;
            }
        }
    }
}
