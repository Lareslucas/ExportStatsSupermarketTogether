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
                Logger.LogInfo("=== MOD EXPORT STATS INICIALIZADO COM SUCESSO (SISTEMA DE BLOQUEIO NATIVO ATIVO) ===");
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
                ProcessarEExecutarSalvamentoDuplo(false); // F10 salva o dia atual da gameplay
            }
        }

        // Intercepta o salvamento automático de 5 em 5 minutos
        [HarmonyPatch(typeof(SaveBehaviour), "SavePersistentValues")]
        [HarmonyPostfix]
        public static void Postfix_SavePersistentValues()
        {
            Instance?.Logger.LogInfo("[Mod Auto-Save] Ciclo de salvamento detectado no jogo. Sincronizando estatísticas...");
            Instance?.ProcessarEExecutarSalvamentoDuplo(false);
        }

        // 🚨 O BLOQUEADOR MÁGICO: Intercepta o fim do dia ANTES de o jogo salvar errado
        [HarmonyPatch(typeof(StatisticsManager), "SaveStatistics")]
        [HarmonyPrefix]
        public static bool Prefix_SaveStatistics()
        {
            Instance?.Logger.LogInfo("[Mod Bloqueador] O jogo tentou salvar as estatísticas de forma instável. Barrando e assumindo o controle...");
            
            // Roda o salvamento forçando a correção do dia retroativo (fim de dia real)
            Instance?.ProcessarEExecutarSalvamentoDuplo(true);
            
            // RETORNA FALSO: Diz ao Unity para IGNORAR a função nativa do jogo. Conflito anulado!
            return false;
        }

        private void ProcessarEExecutarSalvamentoDuplo(bool ehFimDeDia)
        {
            try
            {
                // 1. Localiza a instância ativa do StatisticsManager na memória RAM
                Type tipoStats = Type.GetType("StatisticsManager, Assembly-CSharp");
                object statsInstance = tipoStats?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                
                if (statsInstance == null) return;

                // 2. Captura o Dia Atual do motor de jogo de forma estática ou dinâmica
                int diaAtual = -1;
                Type tipoGameData = Type.GetType("GameData, Assembly-CSharp");
                object gameDataInstance = GameObject.FindObjectOfType(tipoGameData);

                if (gameDataInstance != null)
                {
                    var campoGameDay = tipoGameData.GetField("gameDay", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (campoGameDay != null) diaAtual = Convert.ToInt32(campoGameDay.GetValue(gameDataInstance));
                }
                
                if (diaAtual <= 0)
                {
                    var campoEstaticoDay = tipoGameData?.GetField("gameDay", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (campoEstaticoDay != null) diaAtual = Convert.ToInt32(campoEstaticoDay.GetValue(null));
                }

                // Se falhar em tudo, tenta ler do verificador auxiliar do próprio jogo
                if (diaAtual <= 0)
                {
                    Type tipoCheck = Type.GetType("CheckIfAutosaveExists, Assembly-CSharp");
                    var campoFileDay = tipoCheck?.GetField("fileDay", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (campoFileDay != null) diaAtual = Convert.ToInt32(campoFileDay.GetValue(null));
                }

                // Se mesmo assim não achar o dia atual da sessão, assume o dia 1 de segurança
                if (diaAtual <= 0) diaAtual = 1;

                // CORREÇÃO DO BUG DO JOGO: Se for fim de dia e o jogo já pulou o relógio para o dia seguinte,
                // nós salvamos os dados acumulados no dia correto (anterior).
                if (ehFimDeDia && diaAtual > 1)
                {
                    Logger.LogInfo($"[Correção] Aplicando recuo de ponteiro: Corrigindo dados salvos de Dia {diaAtual} para Dia {diaAtual - 1}.");
                    diaAtual = diaAtual - 1;
                }

                // 3. Captura o slot de salvamento ativo (X)
                int slotAtivo = 3; 
                Type tipoSaveManager = Type.GetType("SaveManager, Assembly-CSharp");
                if (tipoSaveManager != null)
                {
                    object saveInstance = tipoSaveManager.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                    int? slotDinamico = saveInstance?.GetType().GetField("currentSlot", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(saveInstance) as int?;
                    if (slotDinamico.HasValue) slotAtivo = slotDinamico.Value;
                }

                // Configura as rotas
                string nomeArquivoSave = $"StoreFile{slotAtivo}stats.es3";
                string pastaSavesJogo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "..", "LocalLow", "Nokta Games", "Supermarket Together");
                string rotaSaveOficial = Path.Combine(pastaSavesJogo, nomeArquivoSave);

                string pastaBackupMod = Path.Combine(Paths.PluginPath, "ExportStatsSupermarketTogether", "BackupStats");
                if (!Directory.Exists(pastaBackupMod)) Directory.CreateDirectory(pastaBackupMod);
                string rotaBackupSeguro = Path.Combine(pastaBackupMod, $"Backup_{nomeArquivoSave}.json");

                // 4. Empacota todas as variáveis de estatísticas
                Dictionary<string, string> dadosDia = PackValoresEstatisticas(tipoStats, statsInstance);

                // 5. EFETUA A SELEÇÃO DE GRAVAÇÃO DIRETA NO SAVE DO JOGO
                Type tipoES3 = Type.GetType("ES3, Assembly-CSharp-firstpass") ?? Type.GetType("ES3, Assembly-CSharp");
                if (tipoES3 != null)
                {
                    MethodInfo metodoSaveStr = tipoES3.GetMethod("Save", new Type[] { typeof(string), typeof(object), typeof(string) });
                    foreach (var par in dadosDia)
                    {
                        string chaveComDia = $"day{diaAtual}stat{par.Key}";
                        metodoSaveStr?.Invoke(null, new object[] { chaveComDia, par.Value, rotaSaveOficial });
                    }
                    Logger.LogInfo($"[Sucesso] Gravação 1 Corrigida Injetada no arquivo nativo .es3 do slot {slotAtivo}.");
                }

                // 6. GRAVAÇÃO 2: Salva no Backup Seguro (.json)
                SalvarBackupJsonSeguro(rotaBackupSeguro, diaAtual, dadosDia);
                Logger.LogInfo($"[Sucesso] Gravação 2 (Espelho Seguro) guardada na pasta do mod. Dia Alvo: {diaAtual}");
            }
            catch (Exception ex)
            {
                Logger.LogError("Erro crítico na execução do salvamento duplo/bloqueio: " + ex.Message);
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
