using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace ExportStatsSupermarketTogether
{
    [BepInPlugin("com.lucas.supermarket.exportstats", "Export Stats Supermarket Together", "1.0.0")]
    public class ExportStatsPlugin : BaseUnityPlugin
    {
        private static ExportStatsPlugin Instance;

        private void Awake()
        {
            Instance = this;
            
            try
            {
                var harmony = new Harmony("com.lucas.supermarket.exportstats");
                harmony.PatchAll();
                Logger.LogInfo("=== MOD EXPORT STATS INICIALIZADO COM SUCESSO (SISTEMA AUTOMÁTICO ATIVO) ===");
            }
            catch (Exception ex)
            {
                Logger.LogError("Falha ao inicializar ganchos do Harmony: " + ex.Message);
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F10))
            {
                Logger.LogInfo("[F10] Disparando rotina forçada de backup de estatísticas...");
                ProcessarEExecutarSalvamentoDuplo();
            }
        }

        [HarmonyPatch(typeof(SaveBehaviour), "SavePersistentValues")]
        [HarmonyPostfix]
        public static void Postfix_SavePersistentValues()
        {
            Instance?.Logger.LogInfo("[Mod Auto-Save] Ciclo de salvamento detectado no jogo. Sincronizando estatísticas...");
            Instance?.ProcessarEExecutarSalvamentoDuplo();
        }

        [HarmonyPatch(typeof(StatisticsManager), "SaveStatistics")]
        [HarmonyPostfix]
        public static void Postfix_SaveStatistics()
        {
            Instance?.Logger.LogInfo("[Mod End-Day] Fim de dia detectado. Congelando dados do dia anterior...");
            Instance?.ProcessarEExecutarSalvamentoDuplo();
        }

        private void ProcessarEExecutarSalvamentoDuplo()
        {
            try
            {
                // 1. Localiza a instância ativa do gerenciador de estatísticas na memória RAM
                Type tipoStats = Type.GetType("StatisticsManager, Assembly-CSharp");
                object statsInstance = tipoStats?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                
                if (statsInstance == null)
                {
                    Logger.LogWarning("[Aviso] O StatisticsManager ainda não foi instanciado. Aguarde o jogo carregar por completo.");
                    return;
                }

                // 2. Captura o Dia Atual de forma blindada direto do GameData que está vinculado ao Save do jogo
                int diaAtual = -1;
                Type tipoGameData = Type.GetType("GameData, Assembly-CSharp");
                
                // Tenta localizar qualquer objeto ativo de gerenciamento de dados que o Unity gerou na cena
                object gameDataInstance = GameObject.FindObjectOfType(tipoGameData);

                if (gameDataInstance != null)
                {
                    var campoGameDay = tipoGameData.GetField("gameDay", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (campoGameDay != null)
                    {
                        diaAtual = Convert.ToInt32(campoGameDay.GetValue(gameDataInstance));
                    }
                }
                else
                {
                    // Segunda tentativa: Tenta extrair o dia de forma estática pura caso o motor use persistência singleton
                    var campoEstaticoDay = tipoGameData?.GetField("gameDay", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (campoEstaticoDay != null)
                    {
                        diaAtual = Convert.ToInt32(campoEstaticoDay.GetValue(null));
                    }
                }

                // Proteção final: Se mesmo assim não achar o dia atual da sessão, assume o dia 1 para não perder o backup
                if (diaAtual <= 0)
                {
                    diaAtual = 1;
                    Logger.LogWarning("[Aviso] Não foi possível ler o dia real da RAM. Usando ponteiro padrão (Dia 1) para o backup.");
                }

                // 3. Captura o slot de salvamento ativo (X) analisando o SaveManager nativo
                int slotAtivo = 3; 
                Type tipoSaveManager = Type.GetType("SaveManager, Assembly-CSharp");
                if (tipoSaveManager != null)
                {
                    object saveInstance = tipoSaveManager.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                    int? slotDinamico = saveInstance?.GetType().GetField("currentSlot", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(saveInstance) as int?;
                    if (slotDinamico.HasValue) slotAtivo = slotDinamico.Value;
                }

                // Configura as rotas dos dois arquivos independentes de gravação
                string nomeArquivoSave = $"StoreFile{slotAtivo}stats.es3";
                string pastaSavesJogo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "..", "LocalLow", "Nokta Games", "Supermarket Together");
                string rotaSaveOficial = Path.Combine(pastaSavesJogo, nomeArquivoSave);

                string pastaBackupMod = Path.Combine(Paths.PluginPath, "ExportStatsSupermarketTogether", "BackupStats");
                if (!Directory.Exists(pastaBackupMod)) Directory.CreateDirectory(pastaBackupMod);
                string rotaBackupSeguro = Path.Combine(pastaBackupMod, $"Backup_{nomeArquivoSave}.json");

                // 4. Empacota os valores de todas as variáveis do StatisticsManager
                Dictionary<string, string> dadosDia = PackValoresEstatisticas(tipoStats, statsInstance);

                // 5. GRAVAÇÃO 1: Injeta diretamente no save oficial (.es3) usando o motor do jogo
                Type tipoES3 = Type.GetType("ES3, Assembly-CSharp-firstpass") ?? Type.GetType("ES3, Assembly-CSharp");
                if (tipoES3 != null)
                {
                    MethodInfo metodoSaveStr = tipoES3.GetMethod("Save", new Type[] { typeof(string), typeof(object), typeof(string) });
                    foreach (var par in dadosDia)
                    {
                        string chaveComDia = $"day{diaAtual}stat{par.Key}";
                        metodoSaveStr?.Invoke(null, new object[] { chaveComDia, par.Value, rotaSaveOficial });
                    }
                    Logger.LogInfo($"[Sucesso] Gravação 1 injetada no save oficial do slot {slotAtivo}.");
                }

                // 6. GRAVAÇÃO 2: Salva no arquivo de Backup Espelho (.json) isolado e seguro
                SalvarBackupJsonSeguro(rotaBackupSeguro, diaAtual, dadosDia);
                Logger.LogInfo($"[Sucesso] Gravação 2 (Espelho Seguro) atualizada na pasta do mod. (Dia: {diaAtual})");
            }
            catch (Exception ex)
            {
                Logger.LogError("Erro crítico na execução do salvamento duplo: " + ex.Message);
            }
        }

        private Dictionary<string, string> PackValoresEstatisticas(Type tipoStats, object instance)
        {
            Dictionary<string, string> dict = new Dictionary<string, string>();
            string[] camposSimples = {
                "customers", "benefits", "franchiseExperience", "timesRobbed", "moneySpentOnProducts",
                "notFoundProductsCount", "tooExpensiveProductsCount", "lightCost", "rentCost", "employeesCost",
                "omplainedAboutFilth", "totalsProductsSoldThisDay", "totalProductsAcquiredThisDay",
                "productsPlacedInContainers", "totalBoxesRecycled", "totalBalesRecycled", "totalBoxesAddedToBaler",
                "totalTrashCollected", "stolenProductsCollectedFromFloor", "analyzedCustomers",
                "caughtThievesWhenAnalyzing", "salesMade", "extraProductsSoldThankToSales", "paidInvoices",
                "onlineOrdersMade", "moneyMadeByOnlineOrders", "repairedDevices", "bystandersConvertedIntoCustomers"
            };

            foreach (string nomeCampo in camposSimples)
            {
                var campo = tipoStats.GetField(nomeCampo, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                dict[nomeCampo] = campo?.GetValue(instance)?.ToString() ?? "0";
            }
            return dict;
        }

        private void SalvarBackupJsonSeguro(string rota, int dia, Dictionary<string, string> novosDados)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"Ultima_Atualizacao\": \"{DateTime.Now:dd/MM/yyyy HH:mm:ss}\",");
            sb.AppendLine($"  \"Dia_Atual_Ativo\": {dia},");
            sb.AppendLine($"  \"Estatisticas_Dia_{dia}\": {{");
            
            int total = novosDados.Count;
            int cont = 0;
            foreach(var kvp in novosDados)
            {
                cont++;
                string sufixo = (cont < total) ? "," : "";
                sb.AppendLine($"    \"day{dia}stat{kvp.Key}\": \"{kvp.Value}\"{sufixo}");
            }
            
            sb.AppendLine("  }");
            sb.AppendLine("}");
            File.WriteAllText(rota, sb.ToString(), Encoding.UTF8);
        }
    }
}
