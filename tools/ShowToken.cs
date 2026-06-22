using System;
using System.IO;
using Newtonsoft.Json;
using ExportAzureWiki;

class ShowToken
{
    static void Main()
    {
        try
        {
            // Tentar ler o arquivo config.json criptografado
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

            if (File.Exists(configPath))
            {
                Console.WriteLine("=== Configuração Criptografada (config.json) ===");
                var encryptedContent = File.ReadAllText(configPath);

                try
                {
                    // Descriptografar
                    var decryptedContent = EncryptionHelper.Decrypt(encryptedContent);
                    var config = JsonConvert.DeserializeObject<AppConfig>(decryptedContent);

                    Console.WriteLine($"Organization URL: {config?.OrganizationUrl}");
                    Console.WriteLine($"Project Name: {config?.Projectname}");
                    Console.WriteLine($"Wiki Name: {config?.WikiName}");
                    Console.WriteLine($"Personal Access Token: {config?.PersonalAccessToken}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro ao descriptografar: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("Arquivo config.json não encontrado no diretório atual.");
            }

            // Tentar ler o arquivo wikis.json não criptografado
            var wikisPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ExportAzureWiki",
                "wikis.json");

            if (File.Exists(wikisPath))
            {
                Console.WriteLine("\n=== Configuração das Wikis (wikis.json) ===");
                var json = File.ReadAllText(wikisPath);
                dynamic wikiList = JsonConvert.DeserializeObject(json);

                if (wikiList?.Configurations != null)
                {
                    foreach (var wiki in wikiList.Configurations)
                    {
                        Console.WriteLine($"\nWiki: {wiki.Name}");
                        Console.WriteLine($"Platform: {wiki.Platform}");
                        Console.WriteLine($"Base URL: {wiki.BaseUrl}");

                        if (wiki.AuthenticationData?.Token != null)
                        {
                            Console.WriteLine($"Token: {wiki.AuthenticationData.Token}");
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine($"\nArquivo wikis.json não encontrado em: {wikisPath}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro: {ex.Message}");
        }

        Console.WriteLine("\nPressione qualquer tecla para sair...");
        Console.ReadKey();
    }
}