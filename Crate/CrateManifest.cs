namespace DieselEngineFormats.Crate
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using DieselEngineFormats.Bundle;

    /// <summary>
    ///     One asset record inside a CrateManifest block. Tracks existence/type/
    ///     build-time only -- offset/size lives in the .crate file's own TOC.
    /// </summary>
    public class CrateManifestEntry
    {
        public Idstring Extension { get; set; }

        public Idstring Path { get; set; }

        /// <summary>Windows FILETIME (100ns ticks since 1601-01-01) the source asset was built.</summary>
        public ulong Timestamp { get; set; }

        public DateTime? BuildTime => Timestamp == 0
            ? (DateTime?)null
            : DateTime.FromFileTimeUtc((long)Timestamp);

        /// <summary>
        ///     True if this asset has been overridden by a patch crate.
        /// </summary>
        public bool IsPatchOverride { get; set; }

        public CrateManifestEntry(BinaryReader br)
        {
            Extension = HashIndex.Get(br.ReadUInt64());
            Path = HashIndex.Get(br.ReadUInt64());
            Timestamp = br.ReadUInt64();
            IsPatchOverride = br.ReadUInt64() != 0; // bool in the low byte, rest is padding
        }

        public override string ToString() => $"{Path}.{Extension}";
    }

    /// <summary>
    ///     All the asset records belonging to one .crate file, as listed in a
    ///     CrateManifest.
    /// </summary>
    public class CrateManifestBlock
    {
        /// <summary>Crate file this block describes, e.g. "assets_00_0" (-> assets_00_0.crate).</summary>
        public string CrateName { get; set; }

        public List<CrateManifestEntry> Entries { get; } = new List<CrateManifestEntry>();
    }

    /// <summary>
    ///     A name to bitflag mapping (language/platform variants, e.g. "italian",
    ///     "d3d10"), found standalone in crates.properties and as a trailer in
    ///     crates.shipping_manifest.
    /// </summary>
    public class CratePropertyEntry
    {
        public Idstring Name { get; set; }

        /// <summary>Unique power-of-two flag id for this variant.</summary>
        public uint Flag { get; set; }
    }

    /// <summary>
    ///     Reader for crates.shipping_manifest: a global index of which assets
    ///     exist and what type they are, across every .crate file. No offsets/
    ///     sizes -- open the individual CrateFile to extract data.
    ///
    ///     Layout: global header (12 bytes: version, 64-bit block count),
    ///     then exactly BlockCount blocks (crate name, entry count, that many
    ///     32-byte CrateManifestEntry records), then a trailer: a variable-
    ///     length patch/source list followed by the same property/bitflag table
    ///     as crates.properties (minus the "YURI" magic and padding).
    /// </summary>
    public class CrateManifest : DieselFormat
    {
        public uint Version { get; set; }

        /// <summary>Number of blocks that follow the header.</summary>
        public ulong BlockCount { get; set; }

        public List<CrateManifestBlock> Blocks { get; } = new List<CrateManifestBlock>();

        public List<CratePropertyEntry> Properties { get; } = new List<CratePropertyEntry>();

        public CrateManifest() { }
        public CrateManifest(string filePath) : base(filePath) { }
        public CrateManifest(Stream fileStream) : base(fileStream) { }

        public override void ReadFile(BinaryReader br)
        {
            Version = br.ReadUInt32();
            BlockCount = br.ReadUInt64();

            Blocks.Clear();
            Blocks.Capacity = (int)BlockCount;
            for (ulong b = 0; b < BlockCount; b++)
            {
                string name = ReadCString(br);
                ulong count = br.ReadUInt64();
                var block = new CrateManifestBlock { CrateName = name };
                block.Entries.Capacity = (int)count;
                for (ulong i = 0; i < count; i++)
                    block.Entries.Add(new CrateManifestEntry(br));

                Blocks.Add(block);
            }

            ReadPropertyTrailer(br);
        }

        private void ReadPropertyTrailer(BinaryReader br)
        {
            // The trailer is a patch/source list followed by the property table.
            // The list is a u64 count of records; each record is a name, three
            // u32s, and the name repeated.
            ulong patchCount = br.ReadUInt64();
            for (ulong i = 0; i < patchCount; i++)
            {
                ReadCString(br); // patch name
                br.ReadUInt32();
                br.ReadUInt32();
                br.ReadUInt32();
                ReadCString(br); // patch name, repeated
            }
            br.ReadUInt64(); // reserved (0 in all known files currently)

            ulong count = br.ReadUInt64();
            Properties.Clear();
            Properties.Capacity = (int)count;
            for (ulong i = 0; i < count; i++)
            {
                Properties.Add(new CratePropertyEntry
                {
                    Name = HashIndex.Get(br.ReadUInt64()),
                    Flag = br.ReadUInt32(),
                });
            }

            // The property table is the last thing in the file; if the record
            // shape above is wrong we would land off the end here.
            if (br.BaseStream.Position != br.BaseStream.Length)
                throw new InvalidDataException(
                    $"CrateManifest trailer parse ended at {br.BaseStream.Position}, expected EOF ({br.BaseStream.Length}).");
        }

        private static string ReadCString(BinaryReader br)
        {
            var sb = new StringBuilder();
            byte b;
            while ((b = br.ReadByte()) != 0)
                sb.Append((char)b);
            return sb.ToString();
        }
    }

    /// <summary>
    ///     Reader for the standalone crates.properties file: a "YURI"-magic
    ///     name-to-bitflag table, same data as CrateManifest's trailer but with
    ///     16-byte padded records instead of 12-byte packed ones.
    /// </summary>
    public class CratePropertyList : DieselFormat
    {
        public const uint Magic = 0x49525559; // ASCII "YURI" as little-endian uint32

        public List<CratePropertyEntry> Properties { get; } = new List<CratePropertyEntry>();

        public CratePropertyList() { }
        public CratePropertyList(string filePath) : base(filePath) { }
        public CratePropertyList(Stream fileStream) : base(fileStream) { }

        public override void ReadFile(BinaryReader br)
        {
            uint magic = br.ReadUInt32();
            if (magic != Magic)
                throw new InvalidDataException($"Not a crates.properties file (expected magic 0x{Magic:X8}, got 0x{magic:X8})");

            ulong count = br.ReadUInt64();

            Properties.Clear();
            Properties.Capacity = (int)count;
            for (ulong i = 0; i < count; i++)
            {
                var entry = new CratePropertyEntry
                {
                    Name = HashIndex.Get(br.ReadUInt64()),
                    Flag = br.ReadUInt32(),
                };
                br.ReadUInt32(); // likely padding
                Properties.Add(entry);
            }
        }
    }
}
