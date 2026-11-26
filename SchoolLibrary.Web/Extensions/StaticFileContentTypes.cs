using Microsoft.AspNetCore.StaticFiles;

namespace SchoolLibrary.Extensions;

/// <summary>
/// The types of the static files. The PDF reader (pdf.js in wwwroot/lib/pdfjs) loads files whose extensions
/// ASP.NET Core does not know, and the static files middleware does not serve an unknown type.
/// </summary>
public static class StaticFileContentTypes
{
    public static FileExtensionContentTypeProvider Create()
    {
        var provider = new FileExtensionContentTypeProvider();
        provider.Mappings[".mjs"] = "text/javascript";             // the pdf.js modules
        provider.Mappings[".wasm"] = "application/wasm";           // image decoders (JPEG 2000, JBIG2) and colors
        provider.Mappings[".bcmap"] = "application/octet-stream";  // character maps of non-embedded fonts
        provider.Mappings[".pfb"] = "application/octet-stream";    // the standard fonts (Type 1)
        provider.Mappings[".icc"] = "application/vnd.iccprofile";  // color profile
        return provider;
    }
}
