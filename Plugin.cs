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
                // Inicializa os ganchos do Harmony para monitorar os saves automaticamente
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
            // Tecla F10 configurada para forçar o salvamento manual do log e do backup
            if (Input.GetKeyDown(KeyCode.F10))
            {
                Logger.LogInfo("[F10] Disparando rotina forçada de backup de estatísticas...");
                ProcessarEExecutarSalvamentoDuplo();
            }
        }

        // Intercepta o salvamento automático de 5 em 5 minutos e salvamentos manuais
        [HarmonyPatch(typeof(SaveBehaviour), "SavePersistentValues")]
        [HarmonyPostfix]
        public static void Postfix_SavePersistentValues()
        {
            Instance?.Logger.LogInfo("[Mod Auto-Save] Ciclo de salvamento detectado no jogo. Sincronizando estatísticas...");
            Instance?.ProcessarEExecutarSalvamentoDuplo();
        }

        // Intercepta o encerramento do dia quando o jogo salva as parciais
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
                // 1. Captura o Dia Atual direto do motor do jogo (GameData.gameDay)
                Type tipoGameData = Type.GetType("GameData, Assembly-CSharp");
                if (tipoGameData == null) return;
                
                int diaAtual = Convert.ToInt32(tipoGameData.GetField("gameDay", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)?.GetValue(null) ?? -1);
                if (diaAtual == -1) return;

                // 2. Localiza as estatísticas acumuladas na memória RAM (StatisticsManager)
                Type tipoStats = Type.GetType("StatisticsManager, Assembly-CSharp");
                object statsInstance = tipoStats?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (statsInstance == null) return;

                // 3. Captura o slot de salvamento ativo (X) analisando o SaveManager nativo
                int slotAtivo = 3; // Fallback padrão caso falhe a leitura dinâmica
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
                Logger.LogInfo($"[Sucesso] Gravação 2 (Espelho Seguro) atualizada na pasta do mod.");
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
