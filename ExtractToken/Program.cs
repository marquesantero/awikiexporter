using System;
using System.IO;
using Newtonsoft.Json;
using ExportAzureWiki;

class Program
{
    static void Main()
    {
        try
        {
            // Buscar o arquivo config.json no diretório de execução do ExportAzureWiki
            var configPath = @"C:\Users\hyb\source\repos\ExportAzureWiki\ExportAzureWiki\bin\Debug\net8.0-windows\config.json";

            if (File.Exists(configPath))
            {
                Console.WriteLine("=== INFORMAÇÕES DO TOKEN DA WIKI ===\n");
                var encryptedContent = File.ReadAllText(configPath);

                try
                {
                    // Descriptografar o conteúdo
                    var decryptedContent = EncryptionHelper.Decrypt(encryptedContent);
                    var config = JsonConvert.DeserializeObject<AppConfig>(decryptedContent);

                    if (config != null)
                    {
                        Console.WriteLine($"Organization URL: {config.OrganizationUrl}");
                        Console.WriteLine($"Project Name: {config.Projectname}");
                        Console.WriteLine($"Wiki Name: {config.WikiName}");
                        Console.WriteLine($"\n*** PERSONAL ACCESS TOKEN ***");
                        Console.WriteLine($"{config.PersonalAccessToken}");
                        Console.WriteLine($"******************************\n");

                        // Copiar para a área de transferência
                        if (!string.IsNullOrEmpty(config.PersonalAccessToken))
                        {
                            System.Windows.Forms.Clipboard.SetText(config.PersonalAccessToken);
                            Console.WriteLine("Token copiado para a área de transferência!");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro ao descriptografar: {ex.Message}");
                    Console.WriteLine("\nTentando com o sistema legado...");

                    try
                    {
                        // Tentar com o sistema legado se o novo falhar
                        var decryptedContent = LegacyEncryptionHelper.DecryptLegacy(encryptedContent);
                        var config = JsonConvert.DeserializeObject<AppConfig>(decryptedContent);

                        if (config != null)
                        {
                            Console.WriteLine($"Organization URL: {config.OrganizationUrl}");
                            Console.WriteLine($"Project Name: {config.Projectname}");
                            Console.WriteLine($"Wiki Name: {config.WikiName}");
                            Console.WriteLine($"\n*** PERSONAL ACCESS TOKEN (Sistema Legado) ***");
                            Console.WriteLine($"{config.PersonalAccessToken}");
                            Console.WriteLine($"**********************************************\n");
                        }
                    }
                    catch (Exception legacyEx)
                    {
                        Console.WriteLine($"Erro no sistema legado: {legacyEx.Message}");
                    }
                }
            }
            else
            {
                Console.WriteLine($"Arquivo config.json não encontrado em:\n{configPath}");
            }

            // Verificar também o novo sistema de wikis
            var wikisPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ExportAzureWiki",
                "wikis.json");

            if (File.Exists(wikisPath))
            {
                Console.WriteLine("\n=== WIKIS CONFIGURADAS (Novo Sistema) ===\n");
                var json = File.ReadAllText(wikisPath);
                dynamic wikiList = JsonConvert.DeserializeObject(json);

                if (wikiList?.Configurations != null)
                {
                    foreach (var wiki in wikiList.Configurations)
                    {
                        Console.WriteLine($"Wiki: {wiki.Name}");
                        Console.WriteLine($"Platform: {wiki.Platform}");
                        Console.WriteLine($"Base URL: {wiki.BaseUrl}");

                        if (wiki.AuthenticationData?.Token != null)
                        {
                            Console.WriteLine($"Token: {wiki.AuthenticationData.Token}");
                        }
                        Console.WriteLine("---");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro geral: {ex.Message}");
            Console.WriteLine($"Stack: {ex.StackTrace}");
        }

        Console.WriteLine("\nPressione qualquer tecla para sair...");
        Console.ReadKey();
    }
}