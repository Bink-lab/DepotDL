using System;
namespace DepotDL.CLI
{
    internal static class DownloadTui
    {
        /// <summary>
        /// Number of leading spaces to prepend to each line for centering.
        /// Set by the caller before invoking any Write* methods.
        /// </summary>
        internal static int LeftPad = 0;

        private static string Pad => LeftPad > 0 ? new string(' ', LeftPad) : string.Empty;

        public static void WriteHeader(string appId, int depotCount, string outputPath)
        {
            string pad = Pad;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(pad + "╔════════════════════════════════════════╦═════════════════════════════════════╗");
            Console.Write(pad + "║ ");
            WriteColor(TuiText.Pad("DepotDL Download Queue", 38), ConsoleColor.Cyan);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(" ║ ");
            WriteColor(TuiText.Pad($"APP {appId}", 35), ConsoleColor.Cyan);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(" ║");
            Console.WriteLine(pad + "╠════════════════════════════════════════╩═════════════════════════════════════╣");
            WriteInfoRow("Selected Depots", depotCount.ToString(), ConsoleColor.White);
            WriteInfoRow("Output Folder", TuiText.ShortenPath(outputPath, 55), ConsoleColor.Gray);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(pad + "╚══════════════════════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();
        }

        public static void WriteSetup(string label, string value, ConsoleColor color)
        {
            string pad = Pad;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(pad + "  │ ");
            WriteColor(TuiText.Pad(label, 16), ConsoleColor.DarkCyan);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(" │ ");
            WriteColor(value, color);
            Console.ResetColor();
            Console.WriteLine();
        }

        public static void WriteDepotHeader(string depotId, int index, int total, string? manifestId)
        {
            string pad = Pad;
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(pad + "╔══════════════════════════════════════════════════════════════════════════════╗");
            Console.Write(pad + "║ ");
            WriteColor(TuiText.Pad($"Depot {depotId}", 24), ConsoleColor.Cyan);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(" │ ");
            WriteColor(TuiText.Pad($"Queue {index}/{total}", 16), ConsoleColor.White);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(" │ ");
            WriteColor(TuiText.Pad(string.IsNullOrEmpty(manifestId) ? "Latest manifest" : manifestId, 28), ConsoleColor.Gray);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(" ║");
            Console.WriteLine(pad + "╚══════════════════════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
        }

        public static void WriteStatus(string label, string message, ConsoleColor color)
        {
            string pad = Pad;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(pad + "  │ ");
            WriteColor(TuiText.Pad(label, 16), color);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(" │ ");
            WriteColor(message, ConsoleColor.Gray);
            Console.ResetColor();
            Console.WriteLine();
        }

        public static void DrawProgress(double percentage, string? activeValidationFile, ref int lastLineLength)
        {
            int barWidth = 34;
            int filledWidth = (int)Math.Round(percentage / 100.0 * barWidth);
            if (filledWidth < 0) filledWidth = 0;
            if (filledWidth > barWidth) filledWidth = barWidth;

            string filledBar = new string('█', filledWidth);
            string emptyBar = new string('░', barWidth - filledWidth);
            string validation = string.IsNullOrEmpty(activeValidationFile)
                ? string.Empty
                : $"  validating {TuiText.Shorten(activeValidationFile, 24)}";
            // \r goes to column 0, then pad + content restores centering
            string progressPrefix = "\r" + Pad + "  │ Progress     │ ";
            string progressBody = $"{percentage,5:F1}% [{filledBar}{emptyBar}]{validation}";
            string progressText = progressPrefix + progressBody;

            int maxLen = 110;
            try { maxLen = Console.WindowWidth - 1; } catch { }
            if (progressText.Length > maxLen && maxLen > 10)
            {
                progressText = TuiText.Shorten(progressText, maxLen);
            }

            int currentLength = progressText.Length - 1;
            if (currentLength < lastLineLength)
            {
                progressText += new string(' ', lastLineLength - currentLength);
            }
            lastLineLength = currentLength;

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(progressPrefix);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write(progressText[progressPrefix.Length..]);
            Console.ResetColor();
        }

        public static void ClearProgress(ref int lastLineLength)
        {
            if (lastLineLength > 0)
            {
                Console.Write("\r" + new string(' ', lastLineLength) + "\r");
                lastLineLength = 0;
            }
        }

        public static void WriteFinal(bool success, int totalDepots, int successfulDepots, string outputPath)
        {
            string pad = Pad;
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(pad + "╔══════════════════════════════════════════════════════════════════════════════╗");
            Console.Write(pad + "║ ");
            
            string title = success ? "DOWNLOAD ACTIONS COMPLETED" : "DOWNLOAD ACTIONS FINISHED WITH ERRORS";
            int titlePadding = (76 - title.Length) / 2;
            string paddedTitle = new string(' ', titlePadding) + title + new string(' ', 76 - title.Length - titlePadding);
            
            WriteColor(paddedTitle, success ? ConsoleColor.Green : ConsoleColor.Red);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(" ║");
            Console.WriteLine(pad + "╠══════════════════════════════════════════════════════════════════════════════╣");
            
            WriteInfoRow("Output Folder", TuiText.ShortenPath(outputPath, 55), ConsoleColor.Gray);
            WriteInfoRow("Selected Depots", totalDepots.ToString(), ConsoleColor.White);
            
            string successPct = totalDepots > 0 ? $"{(double)successfulDepots / totalDepots * 100:F1}%" : "0%";
            string statusVal = $"{successfulDepots} / {totalDepots} ({successPct} OK)";
            WriteInfoRow("Successful", statusVal, success ? ConsoleColor.Green : ConsoleColor.Yellow);
            
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(pad + "╚══════════════════════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();
        }

        private static void WriteInfoRow(string key, string value, ConsoleColor valueColor)
        {
            string pad = Pad;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(pad + "║ ");
            WriteColor(TuiText.Pad(key, 18), ConsoleColor.DarkCyan);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(" │ ");
            WriteColor(TuiText.Pad(value, 55), valueColor);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(" ║");
        }

        private static void WriteColor(string text, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.Write(text);
        }

    }
}
