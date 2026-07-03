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
        ///     Same variant bitflag as CrateFileEntry.VariantFlag, but unreliable
        ///     here -- the manifest doesn't list every variant. Use the .crate
        ///     TOCs as the source of truth for variants.
        /// </summary>
        public ulong VariantFlag { get; set; }

        public CrateManifestEntry(BinaryReader br)
        {
            Extension = HashIndex.Get(br.ReadUInt64());
            Path = HashIndex.Get(br.ReadUInt64());
            Timestamp = br.ReadUInt64();
            VariantFlag = br.ReadUInt64();
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
    ///     Layout: global header (12 bytes: version, flags, reserved), then
    ///     repeated blocks (crate name, entry count, that many 32-byte
    ///     CrateManifestEntry records) until an empty crate name, then a
    ///     trailer holding the same property/bitflag table as crates.properties
    ///     (minus the "YURI" magic and padding).
    /// </summary>
    public class CrateManifest : DieselFormat
    {
        public uint Version { get; set; }

        public uint Flags { get; set; }

        public List<CrateManifestBlock> Blocks { get; } = new List<CrateManifestBlock>();

        public List<CratePropertyEntry> Properties { get; } = new List<CratePropertyEntry>();

        public CrateManifest() { }
        public CrateManifest(string filePath) : base(filePath) { }
        public CrateManifest(Stream fileStream) : base(fileStream) { }

        public override void ReadFile(BinaryReader br)
        {
            Version = br.ReadUInt32();
            Flags = br.ReadUInt32();
            br.ReadUInt32(); // reserved

            Blocks.Clear();
            while (true)
            {
                long blockStart = br.BaseStream.Position;
                string name = ReadCString(br);

                if (name.Length == 0)
                {
                    // Empty name means this is the trailer's 16 zero-byte prefix, not a block.
                    br.BaseStream.Position = blockStart + 16;
                    ReadPropertyTrailer(br);
                    break;
                }

                ulong count = br.ReadUInt64();
                var block = new CrateManifestBlock { CrateName = name };
                block.Entries.Capacity = (int)count;
                for (ulong i = 0; i < count; i++)
                    block.Entries.Add(new CrateManifestEntry(br));

                Blocks.Add(block);
            }
        }

        private void ReadPropertyTrailer(BinaryReader br)
        {
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

            uint count = br.ReadUInt32();
            br.ReadUInt32(); // reserved

            Properties.Clear();
            Properties.Capacity = (int)count;
            for (int i = 0; i < count; i++)
            {
                var entry = new CratePropertyEntry
                {
                    Name = HashIndex.Get(br.ReadUInt64()),
                    Flag = br.ReadUInt32(),
                };
                br.ReadUInt32(); // reserved
                Properties.Add(entry);
            }
        }
    }
}
