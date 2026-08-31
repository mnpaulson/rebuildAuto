using System;
using System.Collections.Generic;

namespace RebuildBotPlugin.Models
{
    /// <summary>
    /// Root model for accounts.json.
    /// Supports multi-account credential storage and character mapping.
    /// </summary>
    public class AccountRegistry
    {
        public List<AccountEntry> Accounts { get; set; } = new List<AccountEntry>();

        public bool TryGetAccountForProfile(string profileOrCharName, out AccountEntry account, out CharacterEntry character)
        {
            account = null;
            character = null;
            if (string.IsNullOrWhiteSpace(profileOrCharName)) return false;

            foreach (var acc in Accounts)
            {
                if (acc == null) continue;

                // Match by AccountId
                if (string.Equals(acc.AccountId, profileOrCharName, StringComparison.OrdinalIgnoreCase))
                {
                    account = acc;
                    character = acc.Characters != null && acc.Characters.Count > 0 ? acc.Characters[0] : null;
                    return true;
                }

                // Match by Character Name
                if (acc.Characters != null)
                {
                    foreach (var ch in acc.Characters)
                    {
                        if (ch != null && string.Equals(ch.Name, profileOrCharName, StringComparison.OrdinalIgnoreCase))
                        {
                            account = acc;
                            character = ch;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        public bool TryGetAccountById(string accountId, out AccountEntry account)
        {
            account = null;
            if (string.IsNullOrWhiteSpace(accountId)) return false;

            foreach (var acc in Accounts)
            {
                if (acc != null && string.Equals(acc.AccountId, accountId, StringComparison.OrdinalIgnoreCase))
                {
                    account = acc;
                    return true;
                }
            }
            return false;
        }
    }

    public class AccountEntry
    {
        public string AccountId { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public List<CharacterEntry> Characters { get; set; } = new List<CharacterEntry>();
    }

    public class CharacterEntry
    {
        public string Name { get; set; } = "";
        public int Slot { get; set; } = 0;
    }
}
