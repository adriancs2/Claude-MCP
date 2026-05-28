using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MCP2.Services
{
    /// <summary>
    /// Produces a unified-diff (git-style) view of the differences between two
    /// text inputs. Uses a classic Longest Common Subsequence (LCS) algorithm
    /// with backtracking so unchanged lines align correctly even when content
    /// is inserted or deleted — a single-line insertion at the top of a 600-line
    /// file produces ONE diff hunk, not 599 "shifted" lines.
    ///
    /// Inputs may be supplied as raw strings (<see cref="FromStrings"/>) or as
    /// file paths (<see cref="FromFiles"/>). All inputs are normalized to \r\n
    /// line endings before comparison so line-ending differences don't show up
    /// as noise in the diff.
    ///
    /// Output is git-compatible unified-diff text including --- / +++ headers
    /// and @@ hunk headers, ready to print as a tool result or feed to `patch`.
    /// </summary>
    public static class UnifiedDiff
    {
        // Memory guard: refuse full DP if (m * n) exceeds this. ~25M cells of
        // int[] is ~100 MB — generous, but stops a pathological 50K x 50K file
        // pair from OOMing the host process.
        private const long DefaultMaxDpCells = 25_000_000L;

        // =====================================================================
        // Public entry points
        // =====================================================================

        /// <summary>
        /// Diff two text strings. Returns ready-to-print unified-diff text.
        /// </summary>
        public static string FromStrings(string oldContent, string newContent, DiffOptions options = null)
        {
            return Compute(oldContent ?? "", newContent ?? "", options).UnifiedDiffText;
        }

        /// <summary>
        /// Convenience for edit tools: diff "before" and "after" snapshots of a
        /// single file, with --- / +++ labels automatically set to "{path} (before)"
        /// and "{path} (after)". This is the call most edit tools want — it gives
        /// the user the file path in the header without each caller having to
        /// construct a <see cref="DiffOptions"/> explicitly.
        /// </summary>
        public static string ForEdit(string path, string before, string after)
        {
            var options = new DiffOptions
            {
                OldLabel = path + " (before)",
                NewLabel = path + " (after)"
            };
            return Compute(before ?? "", after ?? "", options).UnifiedDiffText;
        }

        /// <summary>
        /// Diff two files. Both files are read as UTF-8.
        /// </summary>
        public static string FromFiles(string oldFilePath, string newFilePath, DiffOptions options = null)
        {
            if (string.IsNullOrEmpty(oldFilePath))
                throw new ArgumentException("oldFilePath is required", "oldFilePath");
            if (string.IsNullOrEmpty(newFilePath))
                throw new ArgumentException("newFilePath is required", "newFilePath");
            if (!File.Exists(oldFilePath))
                throw new FileNotFoundException("File not found", oldFilePath);
            if (!File.Exists(newFilePath))
                throw new FileNotFoundException("File not found", newFilePath);

            // Default the labels to the file paths so the --- / +++ header is
            // useful when the caller didn't override them.
            var opts = options ?? new DiffOptions();
            if (opts.OldLabel == DiffOptions.DefaultOldLabel) opts.OldLabel = oldFilePath;
            if (opts.NewLabel == DiffOptions.DefaultNewLabel) opts.NewLabel = newFilePath;

            string oldText = File.ReadAllText(oldFilePath, Encoding.UTF8);
            string newText = File.ReadAllText(newFilePath, Encoding.UTF8);
            return Compute(oldText, newText, opts).UnifiedDiffText;
        }

        /// <summary>
        /// Lower-level entry point. Returns a structured result so callers can
        /// branch on identical / too-large / changed without parsing the text.
        /// </summary>
        public static DiffResult Compute(string oldContent, string newContent, DiffOptions options = null)
        {
            options = options ?? new DiffOptions();

            // Normalize line endings to \r\n. After this we split on \r\n only —
            // it's the sole separator in the normalized form.
            string a = FileOperations.NormalizeLineEndings(oldContent ?? "");
            string b = FileOperations.NormalizeLineEndings(newContent ?? "");

            string[] linesA = a.Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] linesB = b.Split(new[] { "\r\n" }, StringSplitOptions.None);

            // Build comparison keys. These are the strings actually used for
            // equality testing inside LCS. We keep the original lines around
            // separately so the output always shows the user's real content.
            string[] keysA = BuildKeys(linesA, options.IgnoreWhitespace, options.IgnoreCase);
            string[] keysB = BuildKeys(linesB, options.IgnoreWhitespace, options.IgnoreCase);

            // Trim common prefix and suffix. These are unchanged so they don't
            // need to participate in LCS. We track the offsets so that final
            // line numbers in hunk headers stay correct.
            int prefix = 0;
            int maxPrefix = Math.Min(keysA.Length, keysB.Length);
            while (prefix < maxPrefix && keysA[prefix] == keysB[prefix])
                prefix++;

            int suffix = 0;
            int maxSuffix = Math.Min(keysA.Length - prefix, keysB.Length - prefix);
            while (suffix < maxSuffix &&
                   keysA[keysA.Length - 1 - suffix] == keysB[keysB.Length - 1 - suffix])
                suffix++;

            int len1 = keysA.Length - prefix - suffix;
            int len2 = keysB.Length - prefix - suffix;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(string.Format("--- {0}\t({1} lines)", options.OldLabel, linesA.Length));
            sb.AppendLine(string.Format("+++ {0}\t({1} lines)", options.NewLabel, linesB.Length));
            sb.AppendLine();

            // Quick bail-out: identical inputs.
            if (len1 == 0 && len2 == 0)
            {
                sb.AppendLine("Files are identical.");
                return new DiffResult
                {
                    Identical = true,
                    TooLarge = false,
                    Adds = 0,
                    Dels = 0,
                    UnifiedDiffText = sb.ToString()
                };
            }

            // Memory guard for pathologically large diffs.
            long maxCells = options.MaxDpCells ?? DefaultMaxDpCells;
            long cells = (long)(len1 + 1) * (long)(len2 + 1);
            if (cells > maxCells)
            {
                sb.AppendLine(string.Format(
                    "Inputs differ, but the changed region is too large for a full diff " +
                    "({0} x {1} = {2:N0} DP cells, limit {3:N0}).",
                    len1, len2, cells, maxCells));
                sb.AppendLine(string.Format(
                    "Common prefix: {0} line(s). Common suffix: {1} line(s).",
                    prefix, suffix));
                return new DiffResult
                {
                    Identical = false,
                    TooLarge = true,
                    Adds = 0,
                    Dels = 0,
                    UnifiedDiffText = sb.ToString()
                };
            }

            // Hash the middle region's keys to ints for fast DP comparisons.
            // Two different strings can collide on hash, so we fall back to a
            // string compare on hash match — but for typical files, this makes
            // the inner loop several times faster.
            int[] hash1 = new int[len1];
            int[] hash2 = new int[len2];
            for (int i = 0; i < len1; i++) hash1[i] = keysA[prefix + i].GetHashCode();
            for (int j = 0; j < len2; j++) hash2[j] = keysB[prefix + j].GetHashCode();

            // Run LCS DP on the middle region only.
            // dp[i, j] = LCS length of keysA[prefix..prefix+i] vs keysB[prefix..prefix+j]
            int[,] dp = new int[len1 + 1, len2 + 1];
            for (int i = 1; i <= len1; i++)
            {
                int h1 = hash1[i - 1];
                string k1 = keysA[prefix + i - 1];
                for (int j = 1; j <= len2; j++)
                {
                    if (h1 == hash2[j - 1] && k1 == keysB[prefix + j - 1])
                        dp[i, j] = dp[i - 1, j - 1] + 1;
                    else
                        dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                }
            }

            // Backtrack to produce the edit script for the middle region.
            // We build it in reverse, then reverse at the end.
            List<DiffOp> ops = new List<DiffOp>();
            {
                int i = len1, j = len2;
                while (i > 0 && j > 0)
                {
                    if (hash1[i - 1] == hash2[j - 1] &&
                        keysA[prefix + i - 1] == keysB[prefix + j - 1])
                    {
                        ops.Add(new DiffOp(DiffKind.Equal, prefix + i - 1, prefix + j - 1));
                        i--; j--;
                    }
                    else if (dp[i - 1, j] >= dp[i, j - 1])
                    {
                        ops.Add(new DiffOp(DiffKind.Delete, prefix + i - 1, -1));
                        i--;
                    }
                    else
                    {
                        ops.Add(new DiffOp(DiffKind.Insert, -1, prefix + j - 1));
                        j--;
                    }
                }
                while (i > 0)
                {
                    ops.Add(new DiffOp(DiffKind.Delete, prefix + i - 1, -1));
                    i--;
                }
                while (j > 0)
                {
                    ops.Add(new DiffOp(DiffKind.Insert, -1, prefix + j - 1));
                    j--;
                }
            }
            ops.Reverse();

            // Now wrap the trimmed common prefix back in as Equal ops, and
            // append the trimmed common suffix the same way. This keeps the
            // hunk-grouper's life simple — it sees one continuous op stream
            // covering BOTH inputs end-to-end.
            List<DiffOp> full = new List<DiffOp>(prefix + ops.Count + suffix);
            for (int p = 0; p < prefix; p++)
                full.Add(new DiffOp(DiffKind.Equal, p, p));
            full.AddRange(ops);
            for (int s = 0; s < suffix; s++)
            {
                int i1 = linesA.Length - suffix + s;
                int i2 = linesB.Length - suffix + s;
                full.Add(new DiffOp(DiffKind.Equal, i1, i2));
            }

            // Count real changes for the summary.
            int adds = 0, dels = 0;
            foreach (var op in full)
            {
                if (op.Kind == DiffKind.Insert) adds++;
                else if (op.Kind == DiffKind.Delete) dels++;
            }

            if (adds == 0 && dels == 0)
            {
                // After IgnoreWhitespace / IgnoreCase the inputs may compare
                // equal even though the raw bytes differ. Tell the caller clearly.
                sb.AppendLine("Files are identical (under the comparison options applied).");
                return new DiffResult
                {
                    Identical = true,
                    TooLarge = false,
                    Adds = 0,
                    Dels = 0,
                    UnifiedDiffText = sb.ToString()
                };
            }

            sb.AppendLine(string.Format("{0} line(s) added, {1} line(s) removed.", adds, dels));
            sb.AppendLine();

            // Group ops into hunks and emit unified-diff output.
            int contextLines = Math.Max(0, options.ContextLines);
            EmitHunks(sb, full, linesA, linesB, contextLines);

            return new DiffResult
            {
                Identical = false,
                TooLarge = false,
                Adds = adds,
                Dels = dels,
                UnifiedDiffText = sb.ToString()
            };
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private static string[] BuildKeys(string[] lines, bool ignoreWhitespace, bool ignoreCase)
        {
            if (!ignoreWhitespace && !ignoreCase)
                return lines;

            string[] keys = new string[lines.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                string s = lines[i];
                if (ignoreWhitespace) s = CollapseWhitespace(s);
                if (ignoreCase) s = s.ToLowerInvariant();
                keys[i] = s;
            }
            return keys;
        }

        private static string CollapseWhitespace(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            // Collapse any run of whitespace into a single space, then trim.
            StringBuilder b = new StringBuilder(s.Length);
            bool inWs = false;
            foreach (char c in s)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!inWs) { b.Append(' '); inWs = true; }
                }
                else
                {
                    b.Append(c);
                    inWs = false;
                }
            }
            return b.ToString().Trim();
        }

        private enum DiffKind { Equal, Insert, Delete }

        private struct DiffOp
        {
            public DiffKind Kind;
            public int Idx1; // index into linesA (or -1 for Insert)
            public int Idx2; // index into linesB (or -1 for Delete)
            public DiffOp(DiffKind k, int i1, int i2) { Kind = k; Idx1 = i1; Idx2 = i2; }
        }

        /// <summary>
        /// Walk the op stream, group consecutive non-Equal ops (plus N lines of
        /// surrounding Equal context) into hunks, and emit unified-diff format.
        /// </summary>
        private static void EmitHunks(StringBuilder sb, List<DiffOp> ops,
            string[] linesA, string[] linesB, int contextLines)
        {
            int n = ops.Count;
            int i = 0;
            while (i < n)
            {
                // Find the next change op.
                while (i < n && ops[i].Kind == DiffKind.Equal) i++;
                if (i >= n) break;

                // Hunk start: back up by contextLines from i.
                int hunkStart = Math.Max(0, i - contextLines);

                // Walk forward, absorbing changes plus any Equal runs shorter
                // than 2*contextLines (otherwise we'd just split into two hunks).
                int hunkEnd = i;
                while (hunkEnd < n)
                {
                    if (ops[hunkEnd].Kind != DiffKind.Equal)
                    {
                        hunkEnd++;
                        continue;
                    }
                    // Count how many Equal ops in a row.
                    int run = 0;
                    while (hunkEnd + run < n && ops[hunkEnd + run].Kind == DiffKind.Equal)
                        run++;
                    if (hunkEnd + run >= n)
                    {
                        // Trailing context — include up to contextLines, then stop.
                        hunkEnd = Math.Min(n, hunkEnd + contextLines);
                        break;
                    }
                    if (run > contextLines * 2)
                    {
                        // Big gap — close this hunk after contextLines of trailing context.
                        hunkEnd += contextLines;
                        break;
                    }
                    // Small gap — absorb it and keep going.
                    hunkEnd += run;
                }

                EmitOneHunk(sb, ops, linesA, linesB, hunkStart, hunkEnd);
                i = hunkEnd;
            }
        }

        private static void EmitOneHunk(StringBuilder sb, List<DiffOp> ops,
            string[] linesA, string[] linesB, int start, int end)
        {
            // Compute the input-A / input-B line ranges this hunk covers.
            int oldStart = -1, oldCount = 0;
            int newStart = -1, newCount = 0;

            for (int k = start; k < end; k++)
            {
                var op = ops[k];
                if (op.Kind == DiffKind.Equal || op.Kind == DiffKind.Delete)
                {
                    if (oldStart < 0) oldStart = op.Idx1;
                    oldCount++;
                }
                if (op.Kind == DiffKind.Equal || op.Kind == DiffKind.Insert)
                {
                    if (newStart < 0) newStart = op.Idx2;
                    newCount++;
                }
            }

            // Unified diff is 1-indexed. Empty ranges use 0 with count 0.
            int oldDisplay = oldCount == 0 ? 0 : oldStart + 1;
            int newDisplay = newCount == 0 ? 0 : newStart + 1;

            sb.AppendLine(string.Format("@@ -{0},{1} +{2},{3} @@",
                oldDisplay, oldCount, newDisplay, newCount));

            // Emit lines, but within each contiguous run of non-Equal ops,
            // emit ALL Deletes before ALL Inserts. The LCS backtrack can
            // interleave them based on DP tie-breaks, which reads as
            // "added X, removed Y" instead of "old → new". git diff convention
            // is deletes-first within a replace block, so we buffer and reorder.
            int p = start;
            while (p < end)
            {
                if (ops[p].Kind == DiffKind.Equal)
                {
                    sb.AppendLine(" " + linesA[ops[p].Idx1]);
                    p++;
                    continue;
                }

                // Collect a run of consecutive non-Equal ops.
                int runStart = p;
                while (p < end && ops[p].Kind != DiffKind.Equal) p++;

                // Emit all Deletes in the run first, in original order.
                for (int k = runStart; k < p; k++)
                {
                    if (ops[k].Kind == DiffKind.Delete)
                        sb.AppendLine("-" + linesA[ops[k].Idx1]);
                }
                // Then all Inserts in the run, in original order.
                for (int k = runStart; k < p; k++)
                {
                    if (ops[k].Kind == DiffKind.Insert)
                        sb.AppendLine("+" + linesB[ops[k].Idx2]);
                }
            }
        }
    }

    /// <summary>
    /// Options controlling how <see cref="UnifiedDiff"/> compares its inputs and
    /// formats its output.
    /// </summary>
    public class DiffOptions
    {
        internal const string DefaultOldLabel = "old";
        internal const string DefaultNewLabel = "new";

        /// <summary>
        /// Number of unchanged context lines to show around each hunk. Default 3,
        /// matching `git diff`.
        /// </summary>
        public int ContextLines = 3;

        /// <summary>
        /// If true, collapse runs of whitespace and trim line ends before comparing.
        /// Useful for finding semantic changes when only indentation differs.
        /// </summary>
        public bool IgnoreWhitespace = false;

        /// <summary>
        /// If true, perform case-insensitive line comparison.
        /// </summary>
        public bool IgnoreCase = false;

        /// <summary>
        /// Label for the "old" side in the unified-diff --- header. When using
        /// <see cref="UnifiedDiff.FromFiles"/> this defaults to the file path.
        /// </summary>
        public string OldLabel = DefaultOldLabel;

        /// <summary>
        /// Label for the "new" side in the unified-diff +++ header. When using
        /// <see cref="UnifiedDiff.FromFiles"/> this defaults to the file path.
        /// </summary>
        public string NewLabel = DefaultNewLabel;

        /// <summary>
        /// Override the memory guard for the LCS DP table. Null = use service
        /// default (25,000,000 cells, ~100 MB).
        /// </summary>
        public long? MaxDpCells = null;
    }

    /// <summary>
    /// Structured result from <see cref="UnifiedDiff.Compute"/>. Callers can
    /// branch on <see cref="Identical"/> / <see cref="TooLarge"/> to decide
    /// how to phrase a tool's success message, then print
    /// <see cref="UnifiedDiffText"/> for the actual diff.
    /// </summary>
    public class DiffResult
    {
        /// <summary>True when the inputs compare equal under the chosen options.</summary>
        public bool Identical;

        /// <summary>True when the changed region exceeded MaxDpCells and no diff was produced.</summary>
        public bool TooLarge;

        /// <summary>Number of inserted lines (+ lines in the diff).</summary>
        public int Adds;

        /// <summary>Number of removed lines (- lines in the diff).</summary>
        public int Dels;

        /// <summary>
        /// Ready-to-print unified-diff text, including --- / +++ header,
        /// summary line, and @@ hunks. Always non-null.
        /// </summary>
        public string UnifiedDiffText;
    }
}
