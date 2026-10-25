using Server.Command.Command.Base;
using Server.Log;
using Server.Pimp;
using System;
using System.Globalization;

namespace Server.Command.Command
{
    /// <summary>
    /// PiMP consensus warp (plan W4.1, 25 Oct 2026): /warpclock start &lt;rate&gt; [seconds of game time] | stop | status.
    /// Drives the shared warp clock from the console until proposals and votes exist (W4.3).
    /// </summary>
    public class WarpClockCommand : SimpleCommand
    {
        public override bool Execute(string commandArgs)
        {
            var a = (commandArgs ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string result;
            if (a.Length >= 2 && a[0] == "start" && double.TryParse(a[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var rate))
            {
                var ahead = double.NaN;
                if (a.Length >= 3 && !double.TryParse(a[2], NumberStyles.Float, CultureInfo.InvariantCulture, out ahead)) ahead = double.NaN;
                result = PimpConsensusWarp.Start(rate, ahead);
            }
            else if (a.Length >= 1 && a[0] == "stop") result = PimpConsensusWarp.Stop();
            else if (a.Length >= 1 && a[0] == "status") result = PimpConsensusWarp.Status();
            else result = "usage: /warpclock start <rate> [seconds of game time] | stop | status";
            LunaLog.Normal("[PiMP] /warpclock: " + result);
            return true;
        }
    }
}
