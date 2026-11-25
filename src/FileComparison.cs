using System;
using System.Collections.Generic;

namespace ZeroXEdit
{
    // The dialog borrows both documents. Its modal owner keeps open-tab bytes stable while comparing.
    internal sealed class FileComparison
    {
        internal readonly BinaryDocument Left, Right;
        internal readonly long Length;
        private readonly long common;
        private const int BlockSize = 65536;
        private readonly byte[] left = new byte[BlockSize], right = new byte[BlockSize];
        private readonly List<long> blockFirstRanges = new List<long>(), blockFirstOffsets = new List<long>();
        private bool previousDifferent;
        internal long Scanned { get; private set; }
        internal long DifferentBytes { get; private set; }
        internal long Ranges { get; private set; }
        internal bool Complete { get; private set; }

        internal FileComparison(BinaryDocument leftFile, BinaryDocument rightFile)
        {
            Left = leftFile;
            Right = rightFile;
            common = Math.Min(Left.Length, Right.Length);
            Length = Math.Max(Left.Length, Right.Length);
            if (Left == Right) { Scanned = Length; Complete = true; }
        }

        internal void ScanBlock()
        {
            if (Complete) return;
            int n = (int)Math.Min(BlockSize, common - Scanned);
            if (n > 0)
            {
                Left.Read(Scanned, left, 0, n);
                Right.Read(Scanned, right, 0, n);
                long rangesBefore = Ranges;
                for (int i = 0; i < n; i++)
                {
                    bool different = left[i] != right[i];
                    if (different)
                    {
                        DifferentBytes++;
                        if (!previousDifferent)
                        {
                            if (Ranges == rangesBefore) IndexBlock(Scanned + i);
                            Ranges++;
                        }
                    }
                    previousDifferent = different;
                }
                Scanned += n;
            }
            if (Scanned == common)
            {
                if (Length > common)
                {
                    DifferentBytes += Length - common;
                    if (!previousDifferent) { IndexBlock(common); Ranges++; }
                }
                Scanned = Length;
                Complete = true;
            }
        }

        // Index only the first new range in each block. Even alternating changed bytes
        // need just two 64-bit index entries per 64 KiB, and a jump rereads at most one block.
        private void IndexBlock(long offset)
        {
            blockFirstRanges.Add(Ranges);
            blockFirstOffsets.Add(offset);
        }
        internal long RangeStart(long range)
        {
            if (range < 0 || range >= Ranges) throw new ArgumentOutOfRangeException("range");
            int block = blockFirstRanges.BinarySearch(range);
            if (block >= 0) return blockFirstOffsets[block];
            block = ~block - 1;
            long start = blockFirstOffsets[block], ordinal = blockFirstRanges[block];
            int n = (int)Math.Min(BlockSize - start % BlockSize, common - start);
            Left.Read(start, left, 0, n);
            Right.Read(start, right, 0, n);
            bool different = true;
            for (int i = 1; i < n; i++)
            {
                bool next = left[i] != right[i];
                if (next && !different && ++ordinal == range) return start + i;
                different = next;
            }
            throw new InvalidOperationException("The compared bytes changed during navigation.");
        }
    }
}
