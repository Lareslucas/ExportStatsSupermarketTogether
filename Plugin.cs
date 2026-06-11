using System;
using System.Reflection;
using BepInEx;
using UnityEngine;

namespace ExportadorEstatisticasSupermarket
{
    [BepInPlugin("com.lucas.supermarket.estatisticas", "Exportador de Estatisticas Real", "1.0.0")]
    public class ExportadorPlugin : BaseUnityPlugin
    {
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F10))
            {
                ExecutarSuperScannerGeral();
            }
        }

        private void ExecutarSuperScannerGeral()
        {
            Logger.LogInfo("==================================================================");
            Logger.LogInfo("===   INICIANDO SUPER SCANNER RAIO-X (ARQUITETURA DO JOGO)   ===");
            Logger.LogInfo("==================================================================");

            try
            {
                Assembly assemblyJuego = Assembly.Load("Assembly-CSharp");
                if (assemblyJuego == null)
                {
                    Logger.LogError("Erro: Não foi possível carregar a DLL principal 'Assembly-CSharp'.");
                    return;
                }

                // Palavras-chave para monitorar o presente e mapear o futuro do seu Dashboard
                string[] palavrasChave = { "product", "stock", "steal", "thief", "rob", "recover", "bill", "tax", "expense", "money", "stat", "economy", "finance" };

                int classesEncontradas = 0;

                foreach (Type tipo in assemblyJuego.GetTypes())
                {
                    string nomeClasseMinusculo = tipo.Name.ToLower();
                    bool classeMatches = false;

                    // Verifica se o nome da classe bate com alguma palavra-chave
                    foreach (string palavra in palavrasChave)
                    {
                        if (nomeClasseMinusculo.Contains(palavra))
                        {
                            classeMatches = true;
                            break;
                        }
                    }

                    if (classeMatches)
                    {
                        classesEncontradas++;
                        Logger.LogInfo($"\n[CLASSE DETECTADA]: {tipo.Name}");

                        // Vasculha as variáveis internas (Campos/Fields) dessa classe para achar os contadores ocultos
                        FieldInfo[] campos = tipo.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                        foreach (FieldInfo campo in campos)
                        {
                            Logger.LogInfo($"   ├── -> [VARIÁVEL]: {campo.Name} ({campo.FieldType.Name})");
                        }

                        // Vasculha os métodos (Funções de ação) dessa classe (ex: cobrar conta, registrar roubo)
                        MethodInfo[] metodos = tipo.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                        foreach (MethodInfo metodo in metodos)
                        {
                            // Ignora métodos padrões repetitivos do C# para o log ficar limpo
                            if (metodo.DeclaringType == tipo)
                            {
                                Logger.LogInfo($"   └── [MÉTODO]: {metodo.Name}()");
                            }
                        }
                    }
                }

                Logger.LogInfo("\n==================================================================");
                Logger.LogInfo($"=== SUPER SCANNER CONCLUÍDO! Mapeadas {classesEncontradas} classes de interesse ===");
                Logger.LogInfo("==================================================================");
            }
            catch (Exception ex)
            {
                Logger.LogError("Erro crítico durante a varredura de Engenharia Reversa: " + ex.Message);
            }
        }
    }
}
