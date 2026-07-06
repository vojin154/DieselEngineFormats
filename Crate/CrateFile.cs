namespace DieselEngineFormats.Crate
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using DieselEngineFormats.Bundle;
    using DieselEngineFormats.Utils;

    /// <summary>
    ///     A single entry in a .crate file's table of contents.
    /// </summary>
    public class CrateFileEntry
    {
        /// <summary>
        ///     The asset's type/extension (e.g. "texture", "unit", "lua").
        /// </summary>
        public Idstring Extension { get; set; }

        /// <summary>
        ///     The asset's full path.
        /// </summary>
        public Idstring Path { get; set; }

        /// <summary>
        ///     Absolute byte offset of this entry's data within the .crate file.
        /// </summary>
        public ulong Offset { get; set; }

        /// <summary>
        ///     Size of the entry after decompression.
        /// </summary>
        public ulong RawSize { get; set; }

        /// <summary>
        ///     Compressed size on disk. Zero means stored uncompressed (movie/
        ///     stream/animation data)
        /// </summary>
        public ulong StoredSize { get; set; }

        /// <summary>
        ///     A single bitflag identifying a language/platform variant (e.g.
        ///     "english", "d3d11") of the same path -- the same (Extension,
        ///     Path) pair can appear as multiple TOC entries, one per variant.
        ///     Resolve via crates.properties / CratePropertyList (flag ->
        ///     name), not HashIndex.
        /// </summary>
        public uint VariantFlag { get; set; }

        public CrateFile Parent { get; set; }

        public CrateFileEntry() { }

        public CrateFileEntry(BinaryReader br)
        {
            ReadEntry(br);
        }

        public void ReadEntry(BinaryReader br)
        {
            Extension = HashIndex.Get(br.ReadUInt64());
            Path = HashIndex.Get(br.ReadUInt64());
            Offset = br.ReadUInt64();
            RawSize = br.ReadUInt64();
            StoredSize = br.ReadUInt64();
            VariantFlag = br.ReadUInt32();
            br.ReadUInt32(); // likely padding
        }

        public void WriteEntry(BinaryWriter bw)
        {
            bw.Write(Extension.Hashed);
            bw.Write(Path.Hashed);
            bw.Write(Offset);
            bw.Write(RawSize);
            bw.Write(StoredSize);
            bw.Write(VariantFlag);
            bw.Write(0u); // likely padding
        }

        public override string ToString()
        {
            string variant = VariantFlag != 0 ? $" variant={VariantFlag:x}" : "";
            return $"{Path}.{Extension} (offset: {Offset}, raw: {RawSize}, stored: {StoredSize}{variant})";
        }
    }

    /// <summary>
    ///     Reader for the self-contained .crate package format. A .crate
    ///     carries its own table of contents and asset data in one file.
    ///
    ///     Layout: header (16 bytes: magic "YAOI", format, 64-bit entry
    ///     count), then `count` * 48-byte CrateFileEntry records, then the
    ///     data region starting at the fixed offset DataRegionStart.
    /// </summary>
    public class CrateFile : DieselFormat
    {
        /// <summary>ASCII "YAOI", read as a little-endian uint32.</summary>
        public const uint Magic = 0x494F4159;

        /// <summary>Every .crate reserves this many bytes up front for its TOC.</summary>
        public const ulong DataRegionStart = 1_000_000;

        /// <summary>Observed constant (65536) across every sampled .crate file.</summary>
        public uint Format { get; set; }

        public List<CrateFileEntry> Entries { get; } = new List<CrateFileEntry>();

        /// <summary>
        ///     Path this CrateFile was loaded from, if constructed via a file path.
        ///     Used to reopen the file on demand in <see cref="ReadEntry(CrateFileEntry)"/>.
        /// </summary>
        public string FilePath { get; private set; }

        public CrateFile() { }

        public CrateFile(string filePath) : base(filePath)
        {
            FilePath = filePath;
        }

        public CrateFile(Stream fileStream) : base(fileStream) { }

        public override void ReadFile(BinaryReader br)
        {
            uint magic = br.ReadUInt32();
            if (magic != Magic)
                throw new InvalidDataException($"Not a .crate file (expected magic 0x{Magic:X8}, got 0x{magic:X8})");

            Format = br.ReadUInt32();
            ulong count = br.ReadUInt64();

            Entries.Clear();
            Entries.Capacity = (int)count;
            for (ulong i = 0; i < count; i++)
                Entries.Add(new CrateFileEntry(br) { Parent = this });
        }

        /// <summary>
        ///     Reads and decompresses an entry's bytes from an already-open
        ///     stream, which is not owned/closed by this method.
        /// </summary>
        public byte[] ReadEntry(CrateFileEntry entry, Stream stream)
        {
            if (entry.RawSize == 0)
                return Array.Empty<byte>();

            if (entry.StoredSize == 0)
                return ReadExact(stream, entry.Offset, entry.RawSize, entry);

            byte[] compressed = ReadExact(stream, entry.Offset, entry.StoredSize, entry);

            using var input = new MemoryStream(compressed);
            using var output = new MemoryStream((int)entry.RawSize);
            General.ZLibDecompress(input, output);

            if ((ulong)output.Length != entry.RawSize)
                throw new InvalidDataException(
                    $"Decompressed size {output.Length} does not match declared raw size {entry.RawSize} for entry {entry}");

            // GetBuffer avoids a copy: output never grew past its RawSize capacity
            // (guaranteed by the length check above), so the backing array is
            // exactly RawSize bytes.
            return output.GetBuffer();
        }

        /// <summary>
        ///     Seeks to <paramref name="offset"/> and reads exactly
        ///     <paramref name="length"/> bytes, validating the range against the
        ///     stream before allocating
        /// </summary>
        private static byte[] ReadExact(Stream stream, ulong offset, ulong length, CrateFileEntry entry)
        {
            if (offset + length > (ulong)stream.Length)
                throw new InvalidDataException(
                    $"Entry {entry} at offset {offset} with length {length} extends past end of stream ({stream.Length})");

            stream.Position = (long)offset;
            byte[] data = new byte[length];
            int readTotal = 0;
            while (readTotal < data.Length)
            {
                int read = stream.Read(data, readTotal, data.Length - readTotal);
                if (read <= 0)
                    throw new EndOfStreamException($"Unexpected end of stream reading entry {entry} at offset {offset}");
                readTotal += read;
            }
            return data;
        }

        /// <summary>
        ///     Reads and decompresses an entry by reopening the file this
        ///     CrateFile was constructed from.
        /// </summary>
        public byte[] ReadEntry(CrateFileEntry entry)
        {
            if (FilePath == null)
                throw new InvalidOperationException(
                    "CrateFile has no associated file path (constructed from a stream) -- use ReadEntry(entry, stream) instead.");

            using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read);
            return ReadEntry(entry, fs);
        }

        public CrateFileEntry EntryFromPathHash(ulong pathHash)
        {
            return Entries.Find(e => e.Path.Hashed == pathHash);
        }
    }
}
