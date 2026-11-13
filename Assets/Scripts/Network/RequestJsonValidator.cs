using System;
using System.Globalization;
using System.Text;

namespace BlackWhale.Network
{
    // JsonUtility can coerce numbers. Reject fractional/overflowed integer DTO fields
    // before decoding so B receives the original integer intent, never a rounded day.
    public static class RequestJsonValidator
    {
        public static void Validate(string json) { new Reader(json).Run(); }
        private sealed class Reader
        {
            private readonly string text;
            private int index;
            public Reader(string text) { this.text = text ?? throw new FormatException("Missing JSON."); }
            public void Run() { Value(null, 0); White(); if (index != text.Length) Fail(); }
            private void White() { while (index < text.Length && char.IsWhiteSpace(text[index])) index++; }
            private bool Take(char value)
            { White(); if (index < text.Length && text[index] == value) { index++; return true; } return false; }
            private void Need(char value) { if (!Take(value)) Fail(); }
            private static void Fail() { throw new FormatException("Invalid request JSON or integer field."); }
            private static bool Integer(string field)
            { return field == "daySeconds" || field == "earlyTurnCoins" || field == "lateTurnCoins" || field == "publicRevealDays" || field == "targetShares" || field == "turn" || field == "historyTurn" || field == "bundleIndex" || field == "startDay" || field == "endDay" || field == "weightBps" || field == "entryDay" || field == "exitDay" || field == "bidAmount"; }
            private void Value(string field, int depth)
            {
                White(); if (depth > 32 || index >= text.Length) Fail();
                char token = text[index];
                if (Integer(field))
                {
                    int start = index;
                    Number();
                    string number = text.Substring(start, index - start);
                    if (field == "bidAmount")
                    { if (!long.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)) Fail(); }
                    else if (!int.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)) Fail();
                    return;
                }
                if (token == '{')
                {
                    index++; if (Take('}')) return;
                    do { string name = String(); Need(':'); Value(name, depth + 1); } while (Take(','));
                    Need('}'); return;
                }
                if (token == '[')
                {
                    index++; if (Take(']')) return;
                    do { Value(null, depth + 1); } while (Take(','));
                    Need(']'); return;
                }
                if (token == '"') { String(); return; }
                if (token == 't') { Literal("true"); return; }
                if (token == 'f') { Literal("false"); return; }
                if (token == 'n') { Literal("null"); return; }
                Number();
            }
            private void Literal(string value)
            {
                if (index + value.Length > text.Length || text.Substring(index, value.Length) != value) Fail();
                index += value.Length;
            }
            private static bool Digit(char c) { return c >= '0' && c <= '9'; }
            private void Digits()
            {
                int start = index;
                while (index < text.Length && Digit(text[index])) index++;
                if (start == index) Fail();
            }
            private void Number()
            {
                White(); if (index < text.Length && text[index] == '-') index++;
                if (index >= text.Length) Fail();
                if (text[index] == '0') index++;
                else Digits();
                if (index < text.Length && text[index] == '.') { index++; Digits(); }
                if (index < text.Length && (text[index] == 'e' || text[index] == 'E'))
                { index++; if (index < text.Length && (text[index] == '+' || text[index] == '-')) index++; Digits(); }
            }
            private string String()
            {
                Need('"'); var value = new StringBuilder();
                while (index < text.Length)
                {
                    char c = text[index++];
                    if (c == '"') return value.ToString();
                    if (c < 32) Fail();
                    if (c != '\\') { value.Append(c); continue; }
                    if (index >= text.Length) Fail();
                    c = text[index++];
                    switch (c)
                    {
                        case '"': case '\\': case '/': value.Append(c); break;
                        case 'b': value.Append('\b'); break;
                        case 'f': value.Append('\f'); break;
                        case 'n': value.Append('\n'); break;
                        case 'r': value.Append('\r'); break;
                        case 't': value.Append('\t'); break;
                        case 'u':
                            ushort code;
                            if (index + 4 > text.Length || !ushort.TryParse(text.Substring(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                throw new FormatException("Invalid JSON escape.");
                            value.Append((char)code); index += 4; break;
                        default: Fail(); break;
                    }
                }
                Fail(); return null;
            }
        }
    }
}
