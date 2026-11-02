using System.Text;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MiGestorBDAccesible.Core.Ayuda
{
    public record SeccionManualModelo(string Id, string Titulo);

    public class ManualModelo
    {
        public string Titulo { get; init; } = "";

        public List<SeccionManualModelo> Secciones { get; init; } = [];

        public string Html { get; init; } = "";
    }

    public static class ManualService
    {
        private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder().UsePipeTables().Build();

        private static ManualModelo? _manual;

        public static string Markdown()
        {
            using var flujo = typeof(ManualService).Assembly.GetManifestResourceStream("MANUAL.md")
                ?? throw new InvalidOperationException("No se encontró el manual.");
            using var lector = new StreamReader(flujo, Encoding.UTF8);
            return lector.ReadToEnd();
        }

        public static ManualModelo Manual() => _manual ??= Convertir(Markdown(), 1);

        public static ManualModelo Convertir(string markdown, int nivelesExtra)
        {
            var documento = Markdig.Markdown.Parse(markdown, _pipeline);
            var titulo = "";
            var secciones = new List<SeccionManualModelo>();

            foreach (var encabezado in documento.Descendants<HeadingBlock>().ToList())
            {
                var texto = Texto(encabezado.Inline);

                if (encabezado.Level == 1)
                {
                    titulo = texto;
                    documento.Remove(encabezado);
                    continue;
                }

                if (encabezado.Level == 2)
                {
                    var id = $"manual-{secciones.Count + 1}";
                    encabezado.GetAttributes().Id = id;
                    secciones.Add(new SeccionManualModelo(id, texto));
                }

                encabezado.Level = Math.Min(encabezado.Level + nivelesExtra, 6);
            }

            return new ManualModelo
            {
                Titulo = titulo,
                Secciones = secciones,
                Html = documento.ToHtml(_pipeline)
            };
        }

        public static string PaginaHtml()
        {
            var manual = Convertir(Markdown(), 0);
            static string C(string texto) => System.Net.WebUtility.HtmlEncode(texto);
            var pagina = new StringBuilder();

            pagina.AppendLine("<!DOCTYPE html>");
            pagina.AppendLine("<html lang=\"es\">");
            pagina.AppendLine("<head>");
            pagina.AppendLine("<meta charset=\"utf-8\">");
            pagina.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            pagina.AppendLine($"<title>{C(manual.Titulo)}</title>");
            pagina.AppendLine("""
                <style>
                :root { color-scheme: light dark; --fondo: #ffffff; --texto: #1b1b1b; --borde: #8a8a8a; --enlace: #0a5fb4; }
                @media (prefers-color-scheme: dark) { :root { --fondo: #1e1e1e; --texto: #f0f0f0; --borde: #9a9a9a; --enlace: #4ea1f3; } }
                body { background: var(--fondo); color: var(--texto); font-family: "Segoe UI", sans-serif; font-size: 1.05rem; line-height: 1.6; max-width: 50rem; margin: 0 auto; padding: 1rem; }
                a { color: var(--enlace); }
                table { border-collapse: collapse; }
                th, td { border: 1px solid var(--borde); padding: 0.3rem 0.6rem; text-align: left; }
                code { font-family: Consolas, monospace; }
                </style>
                """);
            pagina.AppendLine("</head>");
            pagina.AppendLine("<body>");
            pagina.AppendLine("<main>");
            pagina.AppendLine($"<h1>{C(manual.Titulo)}</h1>");
            pagina.AppendLine("<nav aria-labelledby=\"indice\">");
            pagina.AppendLine("<h2 id=\"indice\">Índice</h2>");
            pagina.AppendLine("<ul>");

            foreach (var seccion in manual.Secciones)
            {
                pagina.AppendLine($"<li><a href=\"#{seccion.Id}\">{C(seccion.Titulo)}</a></li>");
            }

            pagina.AppendLine("</ul>");
            pagina.AppendLine("</nav>");
            pagina.Append(manual.Html);
            pagina.AppendLine("</main>");
            pagina.AppendLine("</body>");
            pagina.AppendLine("</html>");
            return pagina.ToString();
        }

        private static string Texto(ContainerInline? contenedor)
        {
            var texto = new StringBuilder();

            foreach (var elemento in contenedor?.Descendants<Inline>() ?? [])
            {
                texto.Append(elemento switch
                {
                    LiteralInline literal => literal.Content.ToString(),
                    CodeInline codigo => codigo.Content,
                    _ => ""
                });
            }

            return texto.ToString();
        }
    }
}
