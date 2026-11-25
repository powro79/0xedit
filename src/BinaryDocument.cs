using System;
using System.Collections.Generic;
using System.IO;

namespace ZeroXEdit
{
    // Pieces refer to the open source file or to immutable ranges in append buffers.
    // History holds slices, so deleting or undoing a large selection does not copy it.
    internal sealed class BinaryDocument : IDisposable
    {
        private sealed class Piece
        {
            internal readonly byte[] Buffer;
            internal readonly long Offset, Count;
            internal Piece(byte[] buffer, long offset, long count)
            {
                Buffer = buffer;
                Offset = offset;
                Count = count;
            }
            internal Piece Slice(long skip, long count)
            {
                return new Piece(Buffer, Offset + skip, count);
            }
        }

        private sealed class Edit
        {
            internal long Offset, RemovedCount, AddedCount, Before, After;
            internal List<Piece> Removed, Added;
        }

        private readonly List<Piece> pieces = new List<Piece>();
        private readonly List<long> starts = new List<long>();
        private readonly Stack<Edit> undo = new Stack<Edit>(), redo = new Stack<Edit>();
        private FileStream source;
        private byte[] appendBuffer;
        private int appendUsed;
        private long revision, savedRevision, serial, savedLength;
        private DateTime savedWriteTime;
        internal long Length { get; private set; }
        internal string FilePath { get; private set; }
        internal string Name { get; private set; }
        internal bool IsExample { get; private set; }
        internal bool Dirty
        {
            get {
                return revision != savedRevision;
            }
        }
        internal bool CanUndo
        {
            get {
                return undo.Count != 0;
            }
        }
        internal bool CanRedo
        {
            get {
                return redo.Count != 0;
            }
        }

        private BinaryDocument(string name)
        {
            Name = name;
        }
        internal static BinaryDocument Create(string name, byte[] data, bool example)
        {
            var document = new BinaryDocument(name);
            document.IsExample = example;
            if (data.Length > 0)
                document.pieces.Add(new Piece((byte[])data.Clone(), 0, data.Length));
            document.Index();
            return document;
        }

        internal static BinaryDocument Open(string path)
        {
            path = Path.GetFullPath(path);
            var document = new BinaryDocument(Path.GetFileName(path));
            try
            {
                document.source = new FileStream(path, FileMode.Open, FileAccess.Read,
                                                 FileShare.Read | FileShare.Delete, 65536, FileOptions.RandomAccess);
                if (document.source.Length > 0)
                    document.pieces.Add(new Piece(null, 0, document.source.Length));
                document.Index();
                document.RecordPath(path);
                return document;
            }
            catch
            {
                document.Dispose();
                throw;
            }
        }

        private void Index()
        {
            starts.Clear();
            Length = 0;
            foreach (var piece in pieces)
            {
                starts.Add(Length);
                Length += piece.Count;
            }
        }

        private int Locate(long position)
        {
            if (position == Length)
                return pieces.Count;
            int found = starts.BinarySearch(position);
            return found >= 0 ? found : ~found - 1;
        }

        internal int Read(long offset, byte[] target, int index, int count)
        {
            if (offset < 0 || offset > Length)
                throw new ArgumentOutOfRangeException("offset");
            int wanted = (int)Math.Min(count, Length - offset), copied = 0;
            for (int i = Locate(offset); copied < wanted; i++)
            {
                var piece = pieces[i];
                long within = offset - starts[i], at = piece.Offset + within;
                int take = (int)Math.Min(wanted - copied, piece.Count - within);
                if (piece.Buffer != null)
                    Buffer.BlockCopy(piece.Buffer, (int)at, target, index + copied, take);
                else
                {
                    source.Position = at;
                    for (int read = 0; read < take;)
                    {
                        int n = source.Read(target, index + copied + read, take - read);
                        if (n == 0)
                            throw new EndOfStreamException();
                        read += n;
                    }
                }
                offset += take;
                copied += take;
            }
            return copied;
        }

        internal byte[] ReadBytes(long offset, int count)
        {
            var bytes = new byte[(int)Math.Min(count, Length - offset)];
            Read(offset, bytes, 0, bytes.Length);
            return bytes;
        }

        private Piece Append(byte[] bytes)
        {
            if (appendBuffer == null || bytes.Length > appendBuffer.Length - appendUsed)
            {
                appendBuffer = new byte[Math.Max(65536, bytes.Length)];
                appendUsed = 0;
            }
            Buffer.BlockCopy(bytes, 0, appendBuffer, appendUsed, bytes.Length);
            var piece = new Piece(appendBuffer, appendUsed, bytes.Length);
            appendUsed += bytes.Length;
            return piece;
        }

        private List<Piece> Range(long start, long count)
        {
            var range = new List<Piece>();
            for (int i = Locate(start); count > 0; i++)
            {
                var piece = pieces[i];
                long skip = start - starts[i], take = Math.Min(count, piece.Count - skip);
                range.Add(skip == 0 && take == piece.Count ? piece : piece.Slice(skip, take));
                count -= take;
                start += take;
            }
            return range;
        }

        private void Splice(long offset, long remove, List<Piece> insert)
        {
            int first = Locate(offset), last = Locate(offset + remove);
            var middle = new List<Piece>();
            if (first < pieces.Count && offset > starts[first])
                middle.Add(pieces[first].Slice(0, offset - starts[first]));
            middle.AddRange(insert);
            if (last < pieces.Count && offset + remove > starts[last])
            {
                long skip = offset + remove - starts[last];
                middle.Add(pieces[last].Slice(skip, pieces[last].Count - skip));
                last++;
            }
            pieces.RemoveRange(first, last - first);
            pieces.InsertRange(first, middle);
            // Coalesce adjacent ranges, including both boundaries of the splice.
            for (int i = Math.Min(first + middle.Count, pieces.Count - 1); i >= Math.Max(1, first); i--)
            {
                var left = pieces[i - 1];
                var right = pieces[i];
                if (left.Buffer == right.Buffer && left.Offset + left.Count == right.Offset)
                {
                    pieces[i - 1] = new Piece(left.Buffer, left.Offset, left.Count + right.Count);
                    pieces.RemoveAt(i);
                }
            }
            Index();
        }

        internal bool Replace(long offset, long remove, byte[] bytes, bool finishHexByte = false)
        {
            if (offset < 0 || remove < 0 || offset > Length || remove > Length - offset)
                throw new ArgumentOutOfRangeException("offset");
            if (remove == bytes.Length && remove <= 256)
            {
                byte[] old = ReadBytes(offset, bytes.Length);
                bool equal = true;
                for (int i = 0; i < old.Length; i++)
                    if (old[i] != bytes[i])
                    {
                        equal = false;
                        break;
                    }
                if (equal)
                    return false;
            }
            var added = new List<Piece>();
            if (bytes.Length != 0)
                added.Add(Append(bytes));
            bool amend = finishHexByte && remove == 1 && bytes.Length == 1 && CanUndo && revision != savedRevision &&
                         undo.Peek().Offset == offset && undo.Peek().AddedCount == 1;
            var edit = amend ? undo.Peek()
                             : new Edit { Offset = offset, Removed = Range(offset, remove), RemovedCount = remove,
                                          Before = revision };
            Splice(offset, remove, added);
            edit.Added = added;
            edit.AddedCount = bytes.Length;
            edit.After = ++serial;
            revision = edit.After;
            if (!amend)
                undo.Push(edit);
            redo.Clear();
            return true;
        }

        internal long Undo()
        {
            var edit = undo.Pop();
            Splice(edit.Offset, edit.AddedCount, edit.Removed);
            revision = edit.Before;
            redo.Push(edit);
            return edit.Offset;
        }
        internal long Redo()
        {
            var edit = redo.Pop();
            Splice(edit.Offset, edit.RemovedCount, edit.Added);
            revision = edit.After;
            undo.Push(edit);
            return edit.Offset + edit.AddedCount;
        }

        private void RecordPath(string path)
        {
            var info = new FileInfo(path);
            savedLength = info.Length;
            savedWriteTime = info.LastWriteTimeUtc;
            FilePath = path;
            Name = info.Name;
            IsExample = false;
        }
        internal bool ChangedOnDisk()
        {
            if (FilePath == null)
                return false;
            var info = new FileInfo(FilePath);
            return !info.Exists || info.Length != savedLength || info.LastWriteTimeUtc != savedWriteTime;
        }

        internal void Save(string path)
        {
            path = Path.GetFullPath(path);
            WriteRange(path, 0, Length);
            RecordPath(path);
            savedRevision = revision;
        }

        internal void WriteRange(string path, long offset, long count)
        {
            if (offset < 0 || count < 0 || offset > Length || count > Length - offset)
                throw new ArgumentOutOfRangeException("offset");
            path = Path.GetFullPath(path);
            var temp = Path.Combine(Path.GetDirectoryName(path), ".0xedit-" + Guid.NewGuid().ToString("N") + ".tmp");
            var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            try
            {
                using (var output = stream)
                {
                    var buffer = new byte[1024 * 1024];
                    for (long written = 0; written < count;)
                    {
                        int n = Read(offset + written, buffer, 0, (int)Math.Min(buffer.Length, count - written));
                        output.Write(buffer, 0, n);
                        written += n;
                    }
                    output.Flush(true);
                }
                if (File.Exists(path))
                    File.Replace(temp, path, null);
                else
                    File.Move(temp, path);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }

        public void Dispose()
        {
            if (source != null)
                source.Dispose();
        }
    }
}
