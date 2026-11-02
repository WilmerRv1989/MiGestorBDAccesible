using System.Text;
using MiGestorBDAccesible.Core.Ayuda;

if (args is not ["exportar-manual", var ruta])
{
    Console.Error.WriteLine("Uso: exportar-manual <ruta del archivo .html>");
    return 1;
}

File.WriteAllText(ruta, ManualService.PaginaHtml(), new UTF8Encoding(true));
Console.WriteLine($"Manual exportado en {ruta}");
return 0;
