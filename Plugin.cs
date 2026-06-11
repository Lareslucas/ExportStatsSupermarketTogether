using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Globalization;
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
            // O mod vai disparar a exportação toda vez que você apertar F9 no jogo
            if (Input.GetKeyDown(KeyCode.F9))
            {
                ExportarDadosParaCSV();
            }
        }

        private void ExportarDadosParaCSV()
        {
            Logger.LogInfo("--- INICIANDO EXPORTAÇÃO COMPLETA DE ESTADO DO MERCADO ---");
            
            // Caminho onde o arquivo será salvo (Dentro de BepInEx/plugins/)
            string rotaArquivo = Path.Combine(Paths.PluginPath, "Estatisticas_Mercado.csv");
            StringBuilder csv = new StringBuilder();

            // Cabeçalho do Banco de Dados Relacional
            csv.AppendLine("ID_Producto;Nombre;Quantidade_Comprada;Quantidade_Vendida;Estoque_Atual");

            // Carrega a DLL principal do jogo que roda na memória RAM
            Assembly assemblyJuego = Assembly.Load("Assembly-CSharp");
            Type tipoCatalogo = assemblyJuego.GetType("ProductListing");
            Type tipoProgreso = assemblyJuego.GetType("ProgressionManager");

            if (tipoCatalogo == null || tipoProgreso == null)
            {
                Logger.LogError("Erro crítico: Classes nativas do jogo não encontradas na memória.");
                return;
            }

            // Encontra os objetos ativos na partida atual
            UnityEngine.Object catalogoObj = UnityEngine.Object.FindFirstObjectByType(tipoCatalogo);
            UnityEngine.Object progresoObj = UnityEngine.Object.FindFirstObjectByType(tipoProgreso);

            if (catalogoObj == null || progresoObj == null)
            {
                Logger.LogWarning("Catálogo ou Progresso não encontrados. Certifique-se de estar DENTRO do jogo salvo.");
                return;
            }

            // Puxa as variáveis originais de dados e estatísticas do jogo por reflexão
            FieldInfo campoListaProdutos = tipoCatalogo.GetField("productsData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            
            // Puxa os dados acumulados direto das estatísticas diárias da RAM (Consertando o bug do arquivo .es3)
            FieldInfo campoProdutosVendidos = progresoObj.GetField("productsSoldList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo campoProdutosComprados = progresoObj.GetField("productsAcquiredList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo campoEstoqueAtual = progresoObj.GetField("productsInStockList", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance); // Se o jogo mantiver essa lista ativa

            System.Collections.IEnumerable listaProdutos = campoListaProdutos.GetValue(catalogoObj) as System.Collections.IEnumerable;
            int[] arrayVendidos = campoProdutosVendidos?.GetValue(progresoObj) as int[];
            int[] arrayComprados = campoProdutosComprados?.GetValue(progresoObj) as int[];
            int[] arrayEstoque = campoEstoqueAtual?.GetValue(progresoObj) as int[];

            int contador = 0;

            foreach (var produto in listaProdutos)
            {
                Type tipoProducto = producto.GetType();
                FieldInfo idField = tipoProducto.GetField("productID", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo prefabField = tipoProducto.GetField("productPrefab", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo brandField = tipoProducto.GetField("productBrand", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                int idInt = (int)(idField?.GetValue(producto) ?? -1);
                if (idInt == -1) continue;

                // Descobre o nome do produto decodificando o modelo 3D nativo do jogo
                string nombre = "Desconhecido";
                UnityEngine.Object prefabObj = prefabField?.GetValue(producto) as UnityEngine.Object;
                if (prefabObj != null)
                {
                    nombre = prefabObj.name;
                    if (nombre.Contains("_")) nombre = nombre.Substring(nombre.IndexOf('_') + 1);
                }
                else if (brandField != null)
                {
                    nombre = brandField.GetValue(producto)?.ToString() ?? "Desconhecido";
                }

                // Limpa o nome para não quebrar a estrutura do CSV
                nombre = nombre.Replace("\"", "").Replace(";", " ").Trim();

                // Captura os dados numéricos correspondentes ao ID do produto nas listas da memória
                int qtdVendida = (arrayVendidos != null && idInt >= 0 && idInt < arrayVendidos.Length) ? arrayVendidos[idInt] : 0;
                int qtdComprada = (arrayComprados != null && idInt >= 0 && idInt < arrayComprados.Length) ? arrayComprados[idInt] : 0;
                
                // Se o jogo não tiver o estoque mapeado em array, tentamos ler via caixas na gôndola, caso contrário usa o fallback
                int qtdEstoque = (arrayEstoque != null && idInt >= 0 && idInt < arrayEstoque.Length) ? arrayEstoque[idInt] : 0;

                // Monta a linha do Banco de Dados usando ponto e vírgula como separador oficial
                csv.AppendLine($"{idInt};\"{nombre}\";{qtdComprada};{qtdVendida};{qtdEstoque}");
                contador++;
            }

            // Grava o arquivo fisicamente no computador
            File.WriteAllText(rotaArquivo, csv.ToString(), Encoding.UTF8);
            Logger.LogInfo($"--- SUCESSO! Banco de dados exportado com {contador} produtos em: {rotaArquivo} ---");
        }
    }
}
