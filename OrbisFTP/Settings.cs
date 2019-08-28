using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OrbisFTP
{
    /// <summary>
    /// This class holds all settings that may be required to setup a FTP server.
    /// </summary>
    [Serializable]
    public class Settings
    {
        public string Banner;

        public bool NeedAuth;

        public List<string> BannedEmails;

        public Dictionary<string, string> UserPasswords;

        public bool AnonymousEnabled;

        public string BaseDirectory;

        /// <summary>
        /// Allows or not the SYST command revealing OS.
        /// </summary>
        public bool AllowSYST;

        public Settings()
        {
            Banner = "";
            NeedAuth = false;
            BannedEmails = new List<string>();
            UserPasswords = new Dictionary<string, string>();
            AnonymousEnabled = false;
            BaseDirectory = "FTP_DIR/";
            AllowSYST = true;
        }
    }
}
