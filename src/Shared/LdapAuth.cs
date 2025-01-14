using System.Text;
using Conductor.Shared.Config;
using Conductor.Shared.Types;
using Novell.Directory.Ldap;

namespace Conductor.Shared;

public static class LdapAuth
{
    public static Result<bool> AuthenticateUser(string username, string password)
    {
        try
        {
            var connectionOptions = new LdapConnectionOptions();
            connectionOptions.ConfigureRemoteCertificateValidationCallback(
                (sender, certificate, chain, sslPolicyErrors) =>
                {
                    if (Settings.LdapVerifyCertificate)
                    {
                        return sslPolicyErrors == System.Net.Security.SslPolicyErrors.None;
                    }
                    else
                    {
                        return true;
                    }
                }
            );

            using var connection = new LdapConnection(connectionOptions);
            connection.SecureSocketLayer = Settings.LdapSsl;

            connection.Connect(Settings.LdapServer, Settings.LdapPort);
            connection.Bind($"{username}@{Settings.LdapDomain}.com", password);

            string[] groups = Settings.LdapGroups.Split("|");
            StringBuilder stringBuilder = new();

            stringBuilder.Append($"(&(sAMAccountName={username})(|");

            foreach (string g in groups)
            {
                stringBuilder.Append($"(memberOf=CN={g},{Settings.LdapGroupDN})");
            }

            stringBuilder.Append("))");


            var results = connection.Search(
                Settings.LdapBaseDn,
                LdapConnection.ScopeSub,
                stringBuilder.ToString(),
                null,
                false
            );

            if (!results.HasMore())
            {
                return false;
            }
            else
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            return new Error(ex.Message, ex.StackTrace);
        }
    }
}