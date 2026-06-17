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

        private void Awake()
        {
            Instance = this;
            
            try
            {
                var harmony = new Harmony("com.lucas.supermarket.exportstats");
                harmony.PatchAll();
                Logger.LogInfo("=== BANCO DE DADOS EXTERNO INICIALIZADO (MODO APENAS LEITURA ATIVO) ===");
            }
            catch (Exception ex)
            {
                Logger.LogError("Falha ao inicializar ganchos do Harmony: " + ex.Message);
            }
        }

        private void Update()
        {
            // 1. Gatilho Manual: Tecla F10
            if (Input.GetKeyDown(KeyCode.F10))
            {
                Logger.LogInfo("[F10] Disparando captura manual para o Banco de Dados...");
                ExecutarFluxoDeCapturaGeral(false);
            }

            // 2. Gatilho Sombra: Sincronização em tempo real com o Auto-Save do Jogo
            VerificarSincroniaAutoSaveJogo();
        }

        // 3. Gatilho de Fim de Dia: Intercepta a virada de página do relatório do jogo
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

                // Se o tempo atual for maior que o anterior, significa que o jogo acabou de resetar o cronômetro (Auto-save rodou!)
                if (ultimoTempoAutoSaveConhecido != -1f && tempoAtualAutoSave > ultimoTempoAutoSaveConhecido)
                {
                    Logger.LogInfo("[Sincronia Sombra] Auto-save do jogo detectado! Sincronizando banco de dados externo imediatamente...");
                    ExecutarFluxoDeCapturaGeral(false);
                }

                ultimoTempoAutoSaveConhecido = tempoAtualAutoSave;
            }
            catch { // Silencioso no Update para não poluir a tela
            }
        }

        private void ExecutarFluxoDeCapturaGeral(bool ehFimDeDia)
        {
            try
            {
                // Localiza o StatisticsManager na RAM
                Type tipoStats = Type.GetType("StatisticsManager, Assembly-CSharp");
                object statsInstance = tipoStats?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (statsInstance == null) return;

                // Captura o dia atual da gameplay ativa
                int diaAtual = ObterDiaAtualDaSessao();
                if (diaAtual <= 0) return;

                // Correção de Ponteiro: Se for fim de dia e o jogo pulou o dia prematuramente, recuamos 1 dia para salvar na gaveta certa
                if (ehFimDeDia && diaAtual > 1)
                {
                    diaAtual -= 1;
                }

                // Captura o slot ativo do save (0 a 4)
                int slotAtivo = ObterSlotSaveAtivo();

                // Define a rota limpa do arquivo .json na pasta do mod
                string pastaBackupMod = Path.Combine(Paths.PluginPath, "ExportStatsSupermarketTogether", "BackupStats");
                if (!Directory.Exists(pastaBackupMod)) Directory.CreateDirectory(pastaBackupMod);
                string rotaJsonMetricas = Path.Combine(pastaBackupMod, $"Backup_StoreFile{slotAtivo}stats.json");

                // Coleta todos os dados nativos e processa a correção matemática dos centavos
                Dictionary<string, string> dadosProntos = ColetarEMatematizarDados(tipoStats, statsInstance);

                // Grava de forma cumulativa sem apagar os dias anteriores
                GravarNoBancoDeDadosJson(rotaJsonMetricas, slotAtivo, diaAtual, dadosProntos);
                
                Logger.LogInfo($"[Sucesso] Banco de dados atualizado. Registro do Dia {diaAtual} guardado com precisão.");
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

        private int ObterSlotSaveAtivo()
        {
            try
            {
                Type tipoSaveManager = Type.GetType("SaveManager, Assembly-CSharp");
                object saveInstance = tipoSaveManager?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                return (saveInstance?.GetType().GetField("currentSlot", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(saveInstance) as int?) ?? 3;
            }
            catch { return 3; }
        }

        private Dictionary<string, string> ColetarEMatematizarDados(Type tipoStats, object instance)
        {
            Dictionary<string, string> dict = new Dictionary<string, string>();
            
            // 1. Variáveis Gerais Nativas
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

            // 2. CORREÇÃO DOS CENTAVOS (Listas de Produtos de Alta Precisão)
            ProcessarListasComCentavosReais(tipoStats, instance, dict);

            return dict;
        }

        private void ProcessarListasComCentavosReais(Type tipoStats, object statsInstance, Dictionary<string, string> dict)
        {
            try
            {
                // Extrai as listas nativas de quantidades do jogo
                var listaVendidos = tipoStats.GetField("productsSoldList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(statsInstance) as IList;
                var listaComprados = tipoStats.GetField("productsAcquiredList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(statsInstance) as IList;

                // Localiza a tabela de preços do jogo (Market ou ProductManager dependendo da build)
                Type tipoProductManager = Type.GetType("ProductManager, Assembly-CSharp") ?? Type.GetType("MarketManager, Assembly-CSharp");
                object pmInstance = tipoProductManager?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

                StringBuilder sbVendidos = new StringBuilder("[");
                StringBuilder sbComprados = new StringBuilder("[");
                StringBuilder sbReceitaReal = new StringBuilder("[");
                StringBuilder sbCustoReal = new StringBuilder("[");

                if (listaVendidos != null && pmInstance != null)
                {
                    // O jogo possui uma lista dinâmica de preços reais do tipo float/decimal indexada por ID de produto
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

                        // Puxa o preço real flutuante da RAM com os centavos
                        if (tabelaPrecos != null && i < tabelaPrecos.Count) precoVendaItem = Convert.ToSingle(tabelaPrecos[i]);
                        if (tabelaCost != null && i < tabelaCost.Count) precoCustoItem = Convert.ToSingle(tabelaCost[i]);

                        // Executa a multiplicação matemática corrigindo o erro do jogo
                        float receitaRealCalculada = qtdVendida * precoVendaItem;
                        float custoRealCalculado = qtdComprada * precoCustoItem;

                        sbVendidos.Append(qtdVendida + (i < listaVendidos.Count - 1 ? "," : ""));
                        sbComprados.Append(qtdComprada + (i < listaVendidos.Count - 1 ? "," : ""));
                        
                        // Injeta os valores corretos com string formatada em ponto flutuante americano para o Excel ler
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

            // Se o arquivo já existir, carrega ele para a memória para fazer o Append
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

            // Monta o bloco do dia atual
            Dictionary<string, object> blocoDia = new Dictionary<string, object>();
            blocoDia["Ultima_Atualizacao"] = DateTime.Now.MakeValue(DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));

            foreach (var par in dadosDia)
            {
                if (par.Value.StartsWith("[")) // Identifica se é uma lista para injetar sem aspas textuais
                {
                    blocoDia[par.Key] = par.Value;
                }
                else
                {
                    blocoDia[par.Key] = $"\"{par.Value}\"";
                }
            }

            // Aloca o dia na gaveta correta da Linha do Tempo (Garante a ordem cronológica linear)
            bancoCompleto[$"Dia_{dia}"] = blocoDia;

            // Transforma o dicionário inteiro em um arquivo JSON identado e limpo
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

        // Deserializador leve nativo para não exigir dependência de DLLs de terceiros no GitHub
        private Dictionary<string, Dictionary<string, object>> DeserializarJsonSimples(string json)
        {
            var resultado = new Dictionary<string, Dictionary<string, object>>();
            try
            {
                string[] linhas = json.Split(new[] { "\"Dia_" }, StringSplitOptions.None);
                for (int i = 1; i < linhas.Length; i++)
                {
                    string blocoDia = linhas[i];
                    string numDiaStr = blocoDia.Split('"')[0];
                    int numDia = int.Parse(numDiaStr);

                    var dadosDia = new Dictionary<string, object>();
                    string[] campos = blocoDia.Split('\n');
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

    public static class ExtensionHelper {
        public static string MakeValue(this DateTime dt, string val) => $"\"{val}\"";
    }
}
