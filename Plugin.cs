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
            if (Input.GetKeyDown(KeyCode.F9))
            {
                EscanearMemoriaDoJogo();
            }
        }

        private void EscanearMemoriaDoJogo()
        {
            Logger.LogInfo("=== INICIANDO ESCANER DE MEMÓRIA (CAÇA-CLASSES) ===");
            try
            {
                Assembly assemblyJuego = Assembly.Load("Assembly-CSharp");
                if (assemblyJuego == null)
                {
                    Logger.LogError("Não foi possível carregar a DLL do jogo.");
                    return;
                }

                foreach (Type tipo in assemblyJuego.GetTypes())
                {
                    // Procura por qualquer classe no jogo que tenha "Product" ou "Progression" ou "Store" no nome
                    if (tipo.Name.Contains("Product") || tipo.Name.Contains("Progression") || tipo.Name.Contains("Store") || tipo.Name.Contains("Stat"))
                    {
                        Logger.LogInfo($"[CLASSE ENCONTRADA]: {tipo.Name}");
                    }
                }
                Logger.LogInfo("=== FIM DO ESCANER DE MEMÓRIA ===");
            }
            catch (Exception ex)
            {
                Logger.LogError("Erro ao escanear: " + ex.Message);
            }
        }
    }
}
