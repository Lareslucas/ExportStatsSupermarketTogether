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
        private float ultimoTempoAutoSaveConhecido = -1f;
        
        // Guarda o Slot real capturado no carregamento
        private static int SlotIdentificadoNoCarregamento = -1;

        private void Awake()
        {
            Instance = this;
            
            try
            {
                var harmony = new Harmony("com.lucas.supermarket.exportstats");
                
                // Aplica os ganchos padrões do arquivo automaticamente
                harmony.PatchAll();

                // 🚨 GANCHO DINÂMICO AUTOMÁTICO: Localiza e intercepta o SaveManager sem quebrar o compilador
                Type tipoSaveManager = Type.GetType("SaveManager, Assembly-CSharp");
                if (tipoSaveManager != null)
                {
                    MethodInfo metodoLoad = tipoSaveManager.GetMethod("LoadGame", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                    MethodInfo metodoSetSlot = tipoSaveManager.GetMethod("SetCurrentSlot", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                    
                    MethodInfo prefixoGenerico = typeof(ExportStatsPlugin).GetMethod(nameof(CapturarSlotDoSaveManager), BindingFlags.NonPublic | BindingFlags.Static);

                    if (metodoLoad != null && prefixoGenerico != null)
                        harmony.Patch(metodoLoad, prefix: new HarmonyMethod(prefixoGenerico));
                        
                    if (metodoSetSlot != null && prefixoGenerico != null)
                        harmony.Patch(metodoSetSlot, prefix: new HarmonyMethod(prefixoGenerico));
                }

                Logger.LogInfo("=== BANCO DE DADOS EXTERNO INICIALIZADO (MODO APENAS LEITURA ATIVO) ===");
            }
            catch (Exception ex)
            {
                Logger.LogError("Falha ao inicializar ganchos do Harmony: " + ex.Message);
            }
        }

        // Método que recebe o primeiro argumento numérico enviado pelo jogo (seja o slotIndex ou slot)
        private static void CapturarSlotDoSaveManager(object[] __args)
        {
            try
            {
                if (__args != null && __args.Length > 0)
                {
                    int slotDetectado = Convert.ToInt32(__args[0]);
                    SlotIdentificadoNoCarregamento = slotDetectado;
                    Instance?.Logger.LogInfo($"[Gatilho Dinâmico] Slot interceptado com sucesso na RAM: {slotDetectado}");
                }
            }
            catch { }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F10))
            {
                Logger.LogInfo("[F10] Disparando captura manual para o Banco de Dados...");
                ExecutarFluxoDeCapturaGeral(false);
            }

            VerificarSincroniaAutoSaveJogo();
        }

        [HarmonyPatch(typeof(StatisticsManager), "SaveStatistics")]
        [HarmonyPostfix]
        public static void Postfix_SaveStatistics()
        {
            Instance?.Logger.LogInfo("[Fim de Dia] Mercado fechado. Capturando fechamento do dia com centavos corrigidos...");
            Instance?.ExecutarFluxoDeCapturaGeral(true);
        }

        private void VerificarSincroniaAutoSaveJogo()
        {
            try
            {
                Type tipoGameData = Type.GetType("GameData, Assembly-CSharp");
                if (tipoGameData == null) return;

                object gameDataInstance = GameObject.FindObjectOfType(tipoGameData);
                if (gameDataInstance == null) return;

                var campoAutoSave = tipoGameData.GetField("nextAutosaveTime", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                if (campoAutoSave == null) return;

                float tempoAtualAutoSave = Convert.ToSingle(campoAutoSave.GetValue(gameDataInstance));

                if (ultimoTempoAutoSaveConhecido != -1f && tempoAtualAutoSave > ultimoTempoAutoSaveConhecido)
                {
                    Logger.LogInfo("[Sincronia Sombra] Auto-save do jogo detectado! Sincronizando banco de dados externo imediatamente...");
                    ExecutarFluxoDeCapturaGeral(false);
                }

                ultimoTempoAutoSaveConhecido = tempoAtualAutoSave;
            }
            catch { }
        }

        private void ExecutarFluxoDeCapturaGeral(bool ehFimDeDia)
        {
            try
            {
                Type tipoStats = Type.GetType("StatisticsManager, Assembly-CSharp");
                object statsInstance = tipoStats?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (statsInstance == null) return;

                int diaAtual = ObterDiaAtualDaSessao();
                if (diaAtual <= 0) return;

                if (ehFimDeDia && diaAtual > 1)
                {
                    diaAtual -= 1;
                }

                int slotAtivo = ObterSlotRealDinamico();

                string pastaBackupMod = Path.Combine(Paths.PluginPath, "ExportStatsSupermarketTogether", "BackupStats");
                if (!Directory.Exists(pastaBackupMod)) Directory.CreateDirectory(pastaBackupMod);
                string rotaJsonMetricas = Path.Combine(pastaBackupMod, $"Backup_StoreFile{slotAtivo}stats.json");

                Dictionary<string, string> dadosProntos = ColetarEMatematizarDados(tipoStats, statsInstance);

                GravarNoBancoDeDadosJson(rotaJsonMetricas, slotAtivo, diaAtual, dadosProntos);
                
                Logger.LogInfo($"[Sucesso] Banco de dados updated. Slot {slotAtivo} | Registro do Dia {diaAtual} guardado.");
            }
            catch (Exception ex)
            {
                Logger.LogError("Erro crítico no fluxo do banco de dados: " + ex.Message);
            }
        }

        private int ObterDiaAtualDaSessao()
        {
            try
            {
                Type tipoGameData = Type.GetType("GameData, Assembly-CSharp");
                object gameDataInstance = GameObject.FindObjectOfType(tipoGameData);
                if (gameDataInstance != null)
                {
                    return Convert.ToInt32(tipoGameData.GetField("gameDay", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(gameDataInstance));
                }
                return Convert.ToInt32(tipoGameData?.GetField("gameDay", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) ?? -1);
            }
            catch { return -1; }
        }

        private int ObterSlotRealDinamico()
        {
            if (SlotIdentificadoNoCarregamento != -1)
            {
                return SlotIdentificadoNoCarregamento;
            }

            try
            {
                Type tipoES3Settings = Type.GetType("ES3Settings, Assembly-CSharp-firstpass") ?? Type.GetType("ES3Settings, Assembly-CSharp");
                if (tipoES3Settings != null)
                {
                    var propriedadeDefault = tipoES3Settings.GetProperty("defaultSettings", BindingFlags.Public | BindingFlags.Static);
                    object defaultSettingsInstance = propriedadeDefault?.GetValue(null);

                    if (defaultSettingsInstance != null)
                    {
                        var campoPath = defaultSettingsInstance.GetType().GetField("path", BindingFlags.Public | BindingFlags.Instance);
                        string caminhoDoSave = campoPath?.GetValue(defaultSettingsInstance) as string;

                        if (!string.IsNullOrEmpty(caminhoDoSave))
                        {
                            foreach (char c in caminhoDoSave)
                            {
                                if (char.IsDigit(c))
                                {
                                    int slotDescoberto = int.Parse(c.ToString());
                                    if (slotDescoberto >= 0 && slotDescoberto <= 9) return slotDescoberto;
                                }
                            }
                        }
                    }
                }

                Type tipoSaveManager = Type.GetType("SaveManager, Assembly-CSharp");
                object saveInstance = tipoSaveManager?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (saveInstance != null)
                {
                    var campoSlotManager = saveInstance.GetType().GetField("currentSlot", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (campoSlotManager != null) return Convert.ToInt32(campoSlotManager.GetValue(saveInstance));
                }
            }
            catch { }
            
            return 0; // Fallback padrão agora é o Slot 0
        }

        private Dictionary<string, string> ColetarEMatematizarDados(Type tipoStats, object instance)
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

            ProcessarListasComCentavosReais(tipoStats, instance, dict);

            return dict;
        }

        private void ProcessarListasComCentavosReais(Type tipoStats, object statsInstance, Dictionary<string, string> dict)
        {
            try
            {
                var listaVendidos = tipoStats.GetField("productsSoldList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(statsInstance) as IList;
                var listaComprados = tipoStats.GetField("productsAcquiredList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(statsInstance) as IList;

                Type tipoProductManager = Type.GetType("ProductManager, Assembly-CSharp") ?? Type.GetType("MarketManager, Assembly-CSharp");
                object pmInstance = tipoProductManager?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

                StringBuilder sbVendidos = new StringBuilder("[");
                StringBuilder sbComprados = new StringBuilder("[");
                StringBuilder sbReceitaReal = new StringBuilder("[");
                StringBuilder sbCustoReal = new StringBuilder("[");

                if (listaVendidos != null && pmInstance != null)
                {
                    var campoPrecosVenda = pmInstance.GetType().GetField("productPrices", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) 
                                        ?? pmInstance.GetType().GetField("prices", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    
                    var campoPrecosCusto = pmInstance.GetType().GetField("productCosts", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                        ?? pmInstance.GetType().GetField("costs", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    IList tabelaPrecos = campoPrecosVenda?.GetValue(pmInstance) as IList;
                    IList tabelaCost = campoPrecosCusto?.GetValue(pmInstance) as IList;

                    for (int i = 0; i < listaVendidos.Count; i++)
                    {
                        int qtdVendida = Convert.ToInt32(listaVendidos[i]);
                        int qtdComprada = (listaComprados != null && i < listaComprados.Count) ? Convert.ToInt32(listaComprados[i]) : 0;

                        float precoVendaItem = 1.0f;
                        float precoCustoItem = 1.0f;

                        if (tabelaPrecos != null && i < tabelaPrecos.Count) precoVendaItem = Convert.ToSingle(tabelaPrecos[i]);
                        if (tabelaCost != null && i < tabelaCost.Count) precoCustoItem = Convert.ToSingle(tabelaCost[i]);

                        float receitaRealCalculada = qtdVendida * precoVendaItem;
                        float custoRealCalculado = qtdComprada * precoCustoItem;

                        sbVendidos.Append(qtdVendida + (i < listaVendidos.Count - 1 ? "," : ""));
                        sbComprados.Append(qtdComprada + (i < listaComprados.Count - 1 ? "," : ""));
                        
                        sbReceitaReal.Append(receitaRealCalculada.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + (i < listaVendidos.Count - 1 ? "," : ""));
                        sbCustoReal.Append(custoRealCalculado.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + (i < listaVendidos.Count - 1 ? "," : ""));
                    }
                }

                sbVendidos.Append("]");
                sbComprados.Append("]");
                sbReceitaReal.Append("]");
                sbCustoReal.Append("]");

                dict["productsSoldList"] = sbVendidos.ToString();
                dict["productsAcquiredList"] = sbComprados.ToString();
                dict["receita_real_produto_com_centavos"] = sbReceitaReal.ToString();
                dict["custo_real_produto_com_centavos"] = sbCustoReal.ToString();
            }
            catch
            {
                dict["receita_real_produto_com_centavos"] = "[]";
                dict["custo_real_produto_com_centavos"] = "[]";
            }
        }

        private void GravarNoBancoDeDadosJson(string rota, int slot, int dia, Dictionary<string, string> dadosDia)
        {
            Dictionary<string, Dictionary<string, object>> bancoCompleto = new Dictionary<string, Dictionary<string, object>>();

            if (File.Exists(rota))
            {
                try
                {
                    string conteudoAntigo = File.ReadAllText(rota);
                    bancoCompleto = DeserializarJsonSimples(conteudoAntigo);
                }
                catch {
                    bancoCompleto = new Dictionary<string, Dictionary<string, object>>();
                }
            }

            Dictionary<string, object> blocoDia = new Dictionary<string, object>();
            blocoDia["Ultima_Atualizacao"] = $"\"{DateTime.Now:dd/MM/yyyy HH:mm:ss}\"";

            foreach (var par in dadosDia)
            {
                if (par.Value.StartsWith("["))
                {
                    blocoDia[par.Key] = par.Value;
                }
                else
                {
                    blocoDia[par.Key] = $"\"{par.Value}\"";
                }
            }

            bancoCompleto[$"Dia_{dia}"] = blocoDia;

            StringBuilder jsonFinal = new StringBuilder();
            jsonFinal.AppendLine("{");
            jsonFinal.AppendLine($"  \"Supermercado_Save_Slot\": {slot},");
            jsonFinal.AppendLine("  \"Historico_Cronologico\": {");

            int totalDias = bancoCompleto.Count;
            int contDias = 0;

            foreach (var diaChave in bancoCompleto)
            {
                contDias++;
                jsonFinal.AppendLine($"    \"{diaChave.Key}\": {{");
                
                int totalCampos = diaChave.Value.Count;
                int contCampos = 0;
                foreach (var campo in diaChave.Value)
                {
                    contCampos++;
                    string sufixo = (contCampos < totalCampos) ? "," : "";
                    jsonFinal.AppendLine($"      \"{campo.Key}\": {campo.Value}{sufixo}");
                }

                string sufixoDia = (contDias < totalDias) ? "," : "";
                jsonFinal.AppendLine($"    }}{sufixoDia}");
            }

            jsonFinal.AppendLine("  }");
            jsonFinal.AppendLine("}");

            File.WriteAllText(rota, jsonFinal.ToString(), Encoding.UTF8);
        }

        private Dictionary<string, Dictionary<string, object>> DeserializarJsonSimples(string json)
        {
            var resultado = new Dictionary<string, Dictionary<string, object>>();
            try
            {
                string[] linhas = json.Split(new[] { "\"Dia_" }, StringSplitOptions.None);
                for (int i = 1; i < linhas.Length; i++)
                {
                    string numDiaStr = linhas[i].Split('"')[0];
                    int numDia = int.Parse(numDiaStr);

                    var dadosDia = new Dictionary<string, object>();
                    string[] campos = linhas[i].Split('\n');
                    foreach (var campo in campos)
                    {
                        if (campo.Contains(":") && !campo.Contains("{") && !campo.Contains("}"))
                        {
                            string[] partes = campo.Split(new[] { ':' }, 2);
                            string chave = partes[0].Replace("\"", "").Trim();
                            string valor = partes[1].Trim().TrimEnd(',');
                            dadosDia[chave] = valor;
                        }
                    }
                    resultado[$"Dia_{numDia}"] = dadosDia;
                }
            }
            catch {}
            return resultado;
        }
    }
}
