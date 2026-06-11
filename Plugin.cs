using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using UnityEngine;

namespace ExportadorEstatisticasSupermarket
{
    [BepInPlugin("com.lucas.supermarket.estatisticas", "Exportador de Estatisticas Real", "1.0.0")]
    public class ExportadorPlugin : BaseUnityPlugin
    {
        private void Update()
        {
            // Dispara a exportação oficial ao pressionar F10 dentro do mercado
            if (Input.GetKeyDown(KeyCode.F10))
            {
                ExportarBancoDeDadosCSV();
            }
        }

        private void ExportarBancoDeDadosCSV()
        {
            Logger.LogInfo("--- INICIANDO EXPORTAÇÃO CORRIGIDA (BASE DE DADOS REAL) ---");
            
            string rotaArquivo = Path.Combine(Paths.PluginPath, "Estatisticas_Mercado.csv");
            StringBuilder csv = new StringBuilder();

            // Estrutura limpa para a planilha do Dashboard
            csv.AppendLine("ID_Produto;Nome;Quantidade_Comprada;Quantidade_Vendida;Estoque_Inexistente_Fallback");

            Assembly assemblyJuego = Assembly.Load("Assembly-CSharp");
            Type tipoCatalogo = assemblyJuego.GetType("ProductListing");
            Type tipoEstatisticas = assemblyJuego.GetType("StatisticsManager");

            if (tipoCatalogo == null || tipoEstatisticas == null)
            {
                Logger.LogError("Erro de correspondência: Classes de memória não encontradas.");
                return;
            }

            // Acessa as instâncias ativas do catálogo e do gerenciador de estatísticas do seu save
            UnityEngine.Object catalogoObj = UnityEngine.Object.FindFirstObjectByType(tipoCatalogo);
            UnityEngine.Object estatisticasObj = UnityEngine.Object.FindFirstObjectByType(tipoEstatisticas);

            if (catalogoObj == null || estatisticasObj == null)
            {
                Logger.LogWarning("Por favor, garanta que você está DENTRO da partida antes de apertar F9.");
                return;
            }

            // Puxa as variáveis de tabelas via Reflexão com os nomes reais do log
            FieldInfo campoProdutos = tipoCatalogo.GetField("productsData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo campoVendidos = tipoEstatisticas.GetField("productsSold", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo campoComprados = tipoEstatisticas.GetField("productsAcquired", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            IEnumerable listaProdutos = campoProdutos.GetValue(catalogoObj) as IEnumerable;
            
            // As estatísticas agora usam List<int> em vez de arrays comuns, tratamos como IList para aceitar qualquer contagem dinâmica
            IList listaVendidos = campoVendidos?.GetValue(estatisticasObj) as IList;
            IList listaComprados = campoComprados?.GetValue(estatisticasObj) as IList;

            int contador = 0;

            foreach (var produto in listaProdutos)
            {
                Type tipoProduto = produto.GetType();
                int idInt = (int)(tipoProduto.GetField("productID", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(produto) ?? -1);
                
                if (idInt == -1) continue;

                // Extrai e limpa o nome do modelo 3D do produto
                string nombre = "Desconhecido";
                UnityEngine.Object prefabObj = tipoProduto.GetField("productPrefab", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(produto) as UnityEngine.Object;
                
                if (prefabObj != null)
                {
                    nombre = prefabObj.name;
                    if (nombre.Contains("_")) nombre = nombre.Substring(nombre.IndexOf('_') + 1);
                }
                else
                {
                    nombre = tipoProduto.GetField("productBrand", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(produto)?.ToString() ?? "Desconhecido";
                }

                nombre = nombre.Replace("\"", "").Replace(";", " ").Trim();

                // Busca as quantidades vendidas e compradas usando o ID como índice da lista
                int qtdVendida = 0;
                if (listaVendidos != null && idInt >= 0 && idInt < listaVendidos.Count)
                {
                    qtdVendida = Convert.ToInt32(listaVendidos[idInt]);
                }

                int qtdComprada = 0;
                if (listaComprados != null && idInt >= 0 && idInt < listaComprados.Count)
                {
                    qtdComprada = Convert.ToInt32(listaComprados[idInt]);
                }

                // Nota técnica: O jogo não possui uma lista de "Estoque Atual" em números na classe de estatísticas. 
                // Deixamos o valor como 0 provisório para não quebrar a coluna da sua planilha nesta versão.
                int qtdEstoque = 0; 

                csv.AppendLine($"{idInt};\"{nombre}\";{qtdComprada};{qtdVendida};{qtdEstoque}");
                contador++;
            }

            File.WriteAllText(rotaArquivo, csv.ToString(), Encoding.UTF8);
            Logger.LogInfo($"--- SUCESSO ABSOLUTO! Arquivo salvo com {contador} itens em: {rotaArquivo} ---");
        }
    }
}
