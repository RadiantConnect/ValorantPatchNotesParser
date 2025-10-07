using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ReverseMarkdown;

namespace ParseValorantNotes
{
    internal class Program
    {
	   
	    private static readonly HttpClient Client = new(new HttpClientHandler()
	    {
		    AllowAutoRedirect = true,
		    AutomaticDecompression = DecompressionMethods.All
	    })
	    {
			DefaultRequestHeaders =
			{
				{"User-Agent", "RadiantConnect 1.0"}
			}
	    };

	    private static readonly Converter Converter = new()
	    {
		    Config =
		    {
			    GithubFlavored = true,
			    RemoveComments = true,
			    UnknownTags = Config.UnknownTagsOption.Drop
		    }
	    };
		
		public static async Task Main(string[] args)
		{
#if DEBUG
			if (args.Length == 0)
				args = ["https://playvalorant.com/en-us/news/game-updates/valorant-patch-notes-11-07/"];
#endif

			if (args.Length == 0) { await Console.Error.WriteLineAsync("Missing url argument"); return; }

			string url = args[0];

			if (string.IsNullOrEmpty(url)) { await Console.Error.WriteLineAsync("Missing url"); return; }
			if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) { await Console.Error.WriteLineAsync("Failed to parse Uri"); return; }

			Root? data = await GetPatchJson(uri);

			if (data == null) { await Console.Error.WriteLineAsync("Failed to get patch data"); return; }

			(string patchText, List<string> assets) = GetPatchText(data);
			string markdownText = Converter.Convert(patchText).Trim();
			string title = data.Props.PageProps.Page.Title;

			markdownText = markdownText[markdownText.IndexOf('#')..];

			await SendDiscordPost(title, markdownText, assets, url);
        }

        private static (string, List<string>)GetPatchText(Root root)
        {
	        IReadOnlyList<Blade> blade = root.Props.PageProps.Page.Blades;
	        IEnumerable<Blade> richText = blade.Where(x => x.Type == "patchNotesRichText");
	        IEnumerable<Blade> richText2 = blade.Where(x => x.Type == "articleRichText");
			List<string> patchText = richText.Select(x => x.RichText.Body).ToList();
			patchText.AddRange(richText2.Select(x => x.RichText.Body));
			List<string> assets = [];
	        assets.AddRange(from patch in patchText from Match match in Regex.Matches(patch, "<img.+src=\"(.+)\">") where match.Success select match.Groups[1].Value);
			return (string.Join("\n", patchText), assets);
		}

        private static async Task<Root?> GetPatchJson(Uri url)
        {
	        string data = await Client.GetStringAsync(url);
	        MatchCollection matches = Regex.Matches(data, "<script.+>(.*)</script>", RegexOptions.Compiled);
	        if (matches.Count == 0) return null;
	        Match match = matches.Last();
	        if (!match.Success) return null;
	        string jsonData = match.Groups[1].Value;
	        return JsonSerializer.Deserialize<Root>(jsonData)!;
		}

        private static async Task SendDiscordPost(string title, string inputData, List<string> assets, string url)
        {
	        string[] sections = inputData.Split("##", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			
	        List<object> embeds =
	        [
		        new
		        {
			        title = $"**{title}**",
					url,
					color = 5814783,
		        }
	        ];

	        foreach (string section in sections)
	        {
				if (section.Length < 30 || section.Contains("Patch Notes Summary")) continue;

				string[] lines = section.Split('\n', StringSplitOptions.RemoveEmptyEntries);

				string sectionTitle = string.Empty;
				string cleanedInput = section;
				
				if (lines.Length > 0 && lines[0].StartsWith('#'))
				{
					sectionTitle = lines[0].TrimStart('#', ' ').Trim();
					cleanedInput = string.Join('\n', lines[1..]).Trim();
				}

				lines = cleanedInput.Split('\n', StringSplitOptions.RemoveEmptyEntries);

				if (lines.Length > 0 && lines[0].StartsWith("- **") && lines[0].Trim().EndsWith("**"))
					lines[0] = lines[0].Replace("- **", "### ").Replace("**", "").Trim();

				lines = lines.Where(x => !x.Contains("![](https")).ToArray();

				cleanedInput = string.Join('\n', lines).Trim();

				embeds.Add(new
		        {
			        title = sectionTitle,
			        description = cleanedInput.Trim(),
			        color = 5814783
		        });
	        }

			if (assets.Count > 0)
				embeds.Add(new
		        {
			        title = "Scraped Images",
			        description = "",
			        color = 5814783,
		        });

			
			foreach (string asset in assets)
	        {
				embeds.Add(new
				{
					title = "",
					image = new { url = asset },
					color = 5814783
				});
	        }

	        embeds.Add(new
	        {
		        title = "",
		        description = "Powered by: RadiantConnect",
		        color = 5814783,
	        });

			List<List<object>> embedSplit = embeds.Select((item, index) => new { item, index })
				.GroupBy(x => x.index / 10)
				.Select(g => g.Select(x => x.item).ToList())
				.ToList();

			foreach (List<object> embedPart in embedSplit)
				await SendPayload(embedPart);
		}

        private static async Task SendPayload(List<object> embeds)
        {
	        var payload = new
	        {
		        embeds,
		        username = "Radiant Connect | Valorant Patch Notes",
		        avatar_url = "https://assets.radiantconnect.ca/valorant/valorant-icon.jpg"
	        };

			string? webhook = Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_URL");

			if (string.IsNullOrEmpty(webhook)) { await Console.Error.WriteLineAsync("Missing DISCORD_WEBHOOK_URL environment variable"); return;}

			HttpResponseMessage response = await Client.SendAsync(new HttpRequestMessage(HttpMethod.Post, webhook)
	        {
		        Content = JsonContent.Create(payload, MediaTypeHeaderValue.Parse("application/json"))
	        });

	        if (!response.IsSuccessStatusCode)
	        {
		        string responseContent = await response.Content.ReadAsStringAsync();
		        throw new Exception($"Discord Post failed with status code {response.StatusCode}: {responseContent}\nPayload:{Convert.ToBase64String(Encoding.ASCII.GetBytes(JsonSerializer.Serialize(payload)))}");
	        }
		}
	}

    public record AiRoot(
	    [property: JsonPropertyName("model")] string Model,
	    [property: JsonPropertyName("created_at")] DateTime? CreatedAt,
	    [property: JsonPropertyName("response")] string Response,
	    [property: JsonPropertyName("done")] bool? Done,
	    [property: JsonPropertyName("done_reason")] string DoneReason,
	    [property: JsonPropertyName("context")] IReadOnlyList<int?> Context,
	    [property: JsonPropertyName("total_duration")] long? TotalDuration,
	    [property: JsonPropertyName("load_duration")] int? LoadDuration,
	    [property: JsonPropertyName("prompt_eval_count")] int? PromptEvalCount,
	    [property: JsonPropertyName("prompt_eval_duration")] int? PromptEvalDuration,
	    [property: JsonPropertyName("eval_count")] int? EvalCount,
	    [property: JsonPropertyName("eval_duration")] long? EvalDuration
    );


	public record Action(
		[property: JsonPropertyName("type")] string Type,
		[property: JsonPropertyName("requiresAuthentication")] bool? RequiresAuthentication,
		[property: JsonPropertyName("payload")] Payload Payload
	);

	public record Analytics(
		[property: JsonPropertyName("contentId")] string ContentId,
		[property: JsonPropertyName("contentLocale")] string ContentLocale,
		[property: JsonPropertyName("rev")] string Rev,
		[property: JsonPropertyName("publishDate")] DateTime? PublishDate
	);

	public record Author(
		[property: JsonPropertyName("name")] string Name
	);

	public record Banner(
		[property: JsonPropertyName("provider")] string Provider,
		[property: JsonPropertyName("type")] string Type,
		[property: JsonPropertyName("dimensions")] Dimensions Dimensions,
		[property: JsonPropertyName("url")] string Url,
		[property: JsonPropertyName("colors")] Colors Colors,
		[property: JsonPropertyName("mimeType")] string MimeType
	);

	public record BetaMetadata(
		[property: JsonPropertyName("urlSuffix")] string UrlSuffix,
		[property: JsonPropertyName("campaignID")] string CampaignId,
		[property: JsonPropertyName("isSurveyRequired")] bool? IsSurveyRequired
	);

	public record Blade(
		[property: JsonPropertyName("type")] string Type,
		[property: JsonPropertyName("productId")] string ProductId,
		[property: JsonPropertyName("rsoModal")] RsoModal RsoModal,
		[property: JsonPropertyName("palette")] string Palette,
		[property: JsonPropertyName("environment")] string Environment,
		[property: JsonPropertyName("locale")] string Locale,
		[property: JsonPropertyName("title")] string Title,
		[property: JsonPropertyName("publishDate")] DateTime? PublishDate,
		[property: JsonPropertyName("banner")] Banner Banner,
		[property: JsonPropertyName("description")] Description Description,
		[property: JsonPropertyName("category")] Category Category,
		[property: JsonPropertyName("socialLinks")] IReadOnlyList<object> SocialLinks,
		[property: JsonPropertyName("tags")] IReadOnlyList<Tag> Tags,
		[property: JsonPropertyName("authors")] IReadOnlyList<Author> Authors,
		[property: JsonPropertyName("richText")] RichText RichText,
		[property: JsonPropertyName("separatorType")] string SeparatorType,
		[property: JsonPropertyName("header")] Header Header,
		[property: JsonPropertyName("content")] Content Content,
		[property: JsonPropertyName("fragmentId")] string FragmentId,
		[property: JsonPropertyName("layout")] string Layout,
		[property: JsonPropertyName("items")] IReadOnlyList<Item> Items
	);

	public record Category(
		[property: JsonPropertyName("title")] string Title,
		[property: JsonPropertyName("machineName")] string MachineName,
		[property: JsonPropertyName("action")] Action Action
	);

	public record Colors(
		[property: JsonPropertyName("primary")] string Primary,
		[property: JsonPropertyName("secondary")] string Secondary,
		[property: JsonPropertyName("label")] string Label
	);

	public record Content(
		[property: JsonPropertyName("media")] Media Media
	);

	public record DatadogData(
		[property: JsonPropertyName("clientToken")] string ClientToken,
		[property: JsonPropertyName("applicationId")] string ApplicationId,
		[property: JsonPropertyName("serviceName")] string ServiceName
	);

	public record Description(
		[property: JsonPropertyName("type")] string Type,
		[property: JsonPropertyName("body")] string Body
	);

	public record Dimensions(
		[property: JsonPropertyName("height")] int? Height,
		[property: JsonPropertyName("width")] int? Width,
		[property: JsonPropertyName("aspectRatio")] double? AspectRatio
	);

	public record Favicon(
		[property: JsonPropertyName("icon")] IReadOnlyList<Icon> Icon
	);

	public record Header(
		[property: JsonPropertyName("title")] string Title,
		[property: JsonPropertyName("description")] Description Description
	);

	public record Hreflang(
		[property: JsonPropertyName("locale")] string Locale,
		[property: JsonPropertyName("url")] string Url
	);

	public record Icon(
		[property: JsonPropertyName("size")] string Size,
		[property: JsonPropertyName("icon")] string IconData
	);

	public record Item(
		[property: JsonPropertyName("title")] string Title,
		[property: JsonPropertyName("publishedAt")] DateTime? PublishedAt,
		[property: JsonPropertyName("action")] Action Action,
		[property: JsonPropertyName("product")] Product Product,
		[property: JsonPropertyName("media")] Media Media,
		[property: JsonPropertyName("description")] Description Description,
		[property: JsonPropertyName("category")] Category Category,
		[property: JsonPropertyName("analytics")] Analytics Analytics
	);

	public record LoginLink(
		[property: JsonPropertyName("title")] string Title,
		[property: JsonPropertyName("accessibilityText")] string AccessibilityText,
		[property: JsonPropertyName("action")] Action Action
	);

	public record Media(
		[property: JsonPropertyName("provider")] string Provider,
		[property: JsonPropertyName("type")] string Type,
		[property: JsonPropertyName("sources")] IReadOnlyList<object> Sources,
		[property: JsonPropertyName("youtubeId")] string YoutubeId,
		[property: JsonPropertyName("dimensions")] Dimensions Dimensions,
		[property: JsonPropertyName("url")] string Url,
		[property: JsonPropertyName("colors")] Colors Colors,
		[property: JsonPropertyName("mimeType")] string MimeType
	);

	public record MetaImage(
		[property: JsonPropertyName("provider")] string Provider,
		[property: JsonPropertyName("type")] string Type,
		[property: JsonPropertyName("dimensions")] Dimensions Dimensions,
		[property: JsonPropertyName("url")] string Url,
		[property: JsonPropertyName("colors")] Colors Colors,
		[property: JsonPropertyName("mimeType")] string MimeType
	);

	public record Page(
		[property: JsonPropertyName("baseUrl")] string BaseUrl,
		[property: JsonPropertyName("url")] string Url,
		[property: JsonPropertyName("title")] string Title,
		[property: JsonPropertyName("description")] string Description,
		[property: JsonPropertyName("metaImage")] MetaImage MetaImage,
		[property: JsonPropertyName("favicon")] Favicon Favicon,
		[property: JsonPropertyName("type")] string Type,
		[property: JsonPropertyName("id")] string Id,
		[property: JsonPropertyName("translationId")] string TranslationId,
		[property: JsonPropertyName("locale")] string Locale,
		[property: JsonPropertyName("blades")] IReadOnlyList<Blade> Blades,
		[property: JsonPropertyName("analytics")] Analytics Analytics,
		[property: JsonPropertyName("displayedPublishDate")] DateTime? DisplayedPublishDate,
		[property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
		[property: JsonPropertyName("theme")] Theme Theme,
		[property: JsonPropertyName("gtmContainerId")] string GtmContainerId,
		[property: JsonPropertyName("osanoPolicyId")] string OsanoPolicyId,
		[property: JsonPropertyName("hreflangs")] IReadOnlyList<Hreflang> Hreflangs
	);

	public record PageProps(
		[property: JsonPropertyName("page")] Page Page,
		[property: JsonPropertyName("environment")] string Environment,
		[property: JsonPropertyName("shouldRenderAvailability")] bool? ShouldRenderAvailability,
		[property: JsonPropertyName("datadogData")] DatadogData DatadogData,
		[property: JsonPropertyName("betaMetadata")] BetaMetadata BetaMetadata,
		[property: JsonPropertyName("pubHubSdkHost")] string PubHubSdkHost,
		[property: JsonPropertyName("vwoAccountId")] string VwoAccountId
	);

	public record Payload(
		[property: JsonPropertyName("url")] string Url
	);

	public record Product(
		[property: JsonPropertyName("title")] string Title,
		[property: JsonPropertyName("machineName")] string MachineName,
		[property: JsonPropertyName("id")] string Id,
		[property: JsonPropertyName("media")] Media Media
	);

	public record Props(
		[property: JsonPropertyName("pageProps")] PageProps PageProps,
		[property: JsonPropertyName("__N_SSG")] bool? Nssg
	);

	public record Query(
		[property: JsonPropertyName("pathArray")] IReadOnlyList<string> PathArray
	);

	public record RichText(
		[property: JsonPropertyName("type")] string Type,
		[property: JsonPropertyName("body")] string Body
	);

	public record Root(
		[property: JsonPropertyName("props")] Props Props,
		[property: JsonPropertyName("page")] string Page,
		[property: JsonPropertyName("query")] Query Query,
		[property: JsonPropertyName("buildId")] string BuildId,
		[property: JsonPropertyName("isFallback")] bool? IsFallback,
		[property: JsonPropertyName("isExperimentalCompile")] bool? IsExperimentalCompile,
		[property: JsonPropertyName("gsp")] bool? Gsp,
		[property: JsonPropertyName("locale")] string Locale,
		[property: JsonPropertyName("locales")] IReadOnlyList<string> Locales,
		[property: JsonPropertyName("defaultLocale")] string DefaultLocale,
		[property: JsonPropertyName("scriptLoader")] IReadOnlyList<object> ScriptLoader
	);

	public record RsoModal(
		[property: JsonPropertyName("rsoModalTitle")] string RsoModalTitle,
		[property: JsonPropertyName("loginPrompt")] string LoginPrompt,
		[property: JsonPropertyName("palette")] string Palette,
		[property: JsonPropertyName("signupLink")] SignupLink SignupLink,
		[property: JsonPropertyName("loginLink")] LoginLink LoginLink,
		[property: JsonPropertyName("signupPrompt")] string SignupPrompt
	);

	public record SignupLink(
		[property: JsonPropertyName("title")] string Title,
		[property: JsonPropertyName("accessibilityText")] string AccessibilityText,
		[property: JsonPropertyName("action")] Action Action
	);

	public record Tag(
		[property: JsonPropertyName("title")] string Title,
		[property: JsonPropertyName("action")] Action Action
	);

	public record Theme(
		[property: JsonPropertyName("id")] string Id
	);
}
