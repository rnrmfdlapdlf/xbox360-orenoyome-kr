using System;
using System.Collections.Generic;

namespace OreNoYomeKoreanPatcher
{
    /// <summary>
    /// Unlocks the leftover developer debug menu (same as tools/debug_menu_patch.py).
    ///
    /// Each heroine start script (entries 101-106) has a state machine whose
    /// state 0 (new game) does
    ///     デバッグオフ = 1
    ///     if プレイモード == 0 ... (straight to normal play)
    /// while the debug menu sits behind
    ///     tmp = デバッグオフ; if tmp == 1 -> normal play, else -> debug menu
    /// which is only reachable from dead code.
    ///
    /// Patch (same length, in place):
    ///   1) state 0: デバッグオフ = 1  ->  = 0
    ///   2) the following 'if' compares プレイモード with 0xff (never true) and
    ///      its jump target becomes the デバッグオフ check, so the menu shows.
    /// </summary>
    internal static class DebugMenuPatcher
    {
        private const uint Imm = 0xFF000000;

        private static readonly byte[] Flag = ShiftJis.Strict.GetBytes("デバッグオフ");
        private static readonly byte[] Mode = ShiftJis.Strict.GetBytes("プレイモード");

        /// <returns>number of scripts patched</returns>
        public static int Apply(Uni2Archive archive)
        {
            int patched = 0;
            foreach (Uni2Archive.Entry entry in archive.Entries)
            {
                if (!Bin.StartsWith(entry.Data, "STCM2B") || Bin.IndexOf(entry.Data, Flag, 0) < 0)
                {
                    continue;
                }

                byte[] result = PatchScript(entry.Data);
                if (result != null)
                {
                    entry.Data = result;
                    patched++;
                }
            }

            return patched;
        }

        private static byte[] ArgString(Stcm2Script.Command c, int ai)
        {
            uint v = c.Args[ai][0];
            if (v >> 24 == 0xFF || v < c.Addr || v >= c.Addr + c.RawLength)
            {
                return null;
            }

            int start = (int)v - c.ExtraStart + 16;
            if (start < 0 || start > c.Extra.Length)
            {
                return null;
            }

            int end = Array.IndexOf(c.Extra, (byte)0, start);
            return Bin.Slice(c.Extra, start, (end < 0 ? c.Extra.Length : end) - start);
        }

        private static bool Is(byte[] value, byte[] expected)
        {
            return value != null && Bin.Equal(value, expected);
        }

        private static byte[] PatchScript(byte[] data)
        {
            Stcm2Script s = new Stcm2Script(data);
            List<Stcm2Script.Command> cm = s.Commands;
            List<int> sets = new List<int>();
            int check = -1;
            for (int i = 0; i < cm.Count - 2; i++)
            {
                Stcm2Script.Command c = cm[i];
                if (c.Kind != 0 || c.Args.Length == 0)
                {
                    continue;
                }

                // state 0:  op33 デバッグオフ = 1  followed by op5 (if) on a register == 0
                if (c.Op == 0x33 && c.Args.Length == 2 && Is(ArgString(c, 0), Flag) && c.Args[1][0] == (Imm | 1))
                {
                    Stcm2Script.Command n = cm[i + 1];
                    if (n.Kind == 0 && n.Op == 5 && n.Args.Length == 4 && n.Args[1][0] == (Imm | 4)
                        && n.Args[2][0] == Imm && n.Args[3][0] == Stcm2Script.Label)
                    {
                        sets.Add(i);
                    }
                }

                // debug check:  op35 デバッグオフ -> reg; op35 プレイモード -> reg; op5 reg == 1
                if (c.Op == 0x35 && Is(ArgString(c, 0), Flag))
                {
                    Stcm2Script.Command n1 = cm[i + 1];
                    Stcm2Script.Command n2 = cm[i + 2];
                    if (n1.Kind == 0 && n1.Op == 0x35 && n1.Args.Length > 0 && Is(ArgString(n1, 0), Mode)
                        && n2.Kind == 0 && n2.Op == 5 && n2.Args.Length > 2 && n2.Args[2][0] == (Imm | 1))
                    {
                        check = c.Addr;
                    }
                }
            }

            if (sets.Count != 1 || check < 0)
            {
                return null;
            }

            byte[] output = (byte[])data.Clone();
            Stcm2Script.Command set = cm[sets[0]];
            Stcm2Script.Command iff = cm[sets[0] + 1];
            Bin.PutU32BE(output, set.Addr + 16 + 12, Imm);              // デバッグオフ = 0
            Bin.PutU32BE(output, iff.Addr + 16 + 24, Imm | 0xFF);       // compare with 0xff
            Bin.PutU32BE(output, iff.Addr + 16 + 36 + 4, (uint)check);  // jump -> debug check
            return output;
        }
    }
}
