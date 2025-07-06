using System.Text;
using Conductor.Types;
using Novell.Directory.Ldap;

namespace Conductor.Shared;

public static class LdapAuth
{
    public static async Task<Result<bool>> AuthenticateUser(string username, string password)
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

            await connection.ConnectAsync(Settings.LdapServer, Settings.LdapPort);
            await connection.BindAsync($"{username}@{Settings.LdapDomain}.com", password);

            string[] groups = Settings.LdapGroups.Split("|");
            StringBuilder stringBuilder = new();

            stringBuilder.Append($"(&(sAMAccountName={username})(|");

            foreach (string g in groups)
            {
                stringBuilder.Append($"(memberOf=CN={g},{Settings.LdapGroupDN})");
            }

            stringBuilder.Append("))");


            var results = await connection.SearchAsync(
                Settings.LdapBaseDn,
                LdapConnection.ScopeSub,
                stringBuilder.ToString(),
                null,
                false
            );

            if (!await results.HasMoreAsync())
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