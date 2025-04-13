namespace Chickensoft.GodotEnv.Tests;

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Chickensoft.GodotEnv.Common.Clients;
using Chickensoft.GodotEnv.Common.Models;
using CliFx.Exceptions;
using Downloader;
using Moq;
using Shouldly;
using Xunit;

public class NetworkClientTest {

  private const string TEST_URL = "https://httpbin.org/get";

  [Fact]
  public async Task WebRequestGetAsyncUseNoProxy() {
    var mockDownloadService = new Mock<IDownloadService>();
    var downloadConfig = Defaults.DownloadConfiguration;
    var networkClient = new NetworkClient(mockDownloadService.Object, downloadConfig);

    var response = await networkClient.WebRequestGetAsync(TEST_URL, false, null);

    response.ShouldNotBeNull();
    response.StatusCode.ShouldBe(HttpStatusCode.OK);

    response.IsSuccessStatusCode.ShouldBeTrue();
    var content = await response.Content.ReadAsStringAsync();
    content.ShouldContain("\"url\":");
  }

  [Fact]
  public void WebRequestGetAsyncUseInvalidProxyThrowsCommandException() {
    var mockDownloadService = new Mock<IDownloadService>();
    var downloadConfig = Defaults.DownloadConfiguration;
    var invalidProxyUrl = "invalid-proxy-url";

    var networkClient = new NetworkClient(
      mockDownloadService.Object,
      downloadConfig
    );

    Should.Throw<CommandException>(async () => await networkClient.WebRequestGetAsync(TEST_URL, false, invalidProxyUrl)).Message.ShouldContain("Invalid proxy URL");
  }

  [Fact]
  public void WebRequestGetAsyncUseValidProxy() {
    var proxyUrl = "http://proxy.example.com:8080";

    var mockDownloadService = new Mock<IDownloadService>();
    var downloadConfig = Defaults.DownloadConfiguration;

    var networkClient = new TestableNetworkClient(mockDownloadService.Object, downloadConfig);

    var handler = networkClient.PublicCreateHttpClientHandler(proxyUrl);

    handler.UseProxy.ShouldBeTrue();
    handler.Proxy.ShouldNotBeNull();
    var webProxy = handler.Proxy as WebProxy;
    webProxy.ShouldNotBeNull();
    webProxy.Address.ShouldNotBeNull();

    webProxy.Address.Host.ShouldBe("proxy.example.com");
    webProxy.Address.Port.ShouldBe(8080);
    webProxy.Address.Scheme.ShouldBe("http");
  }

  [Fact]
  public void CreateDownloadWithProxy() {
    var url = "https://example.com/file.zip";
    var destinationDirectory = "/tmp/downloads";
    var filename = "test-file.zip";
    var proxyUrl = "http://proxy.example.com:8080";

    var mockDownloadService = new Mock<IDownloadService>();
    var downloadConfig = Defaults.DownloadConfiguration;

    var networkClient = new TestableNetworkClient(mockDownloadService.Object, downloadConfig);

    var download = networkClient.PublicCreateDownloadWithProxy(url, destinationDirectory, filename, proxyUrl);

    var proxy = downloadConfig.RequestConfiguration.Proxy;
    proxy.ShouldNotBeNull();
    var webProxy = proxy as WebProxy;
    webProxy.ShouldNotBeNull();
    webProxy.Address.ShouldNotBeNull();

    webProxy.Address.Host.ShouldBe("proxy.example.com");
    webProxy.Address.Port.ShouldBe(8080);
    webProxy.Address.Scheme.ShouldBe("http");
    webProxy.UseDefaultCredentials.ShouldBeFalse();

    download.ShouldNotBeNull();
    download.Url.ShouldBe(url);
    download.Folder.ShouldBe(destinationDirectory);
    download.Filename.ShouldBe(filename);
  }

  [Fact]
  public async Task DownloadFileAsyncUseNoProxyWithProgress() {
    var testUrl = "https://httpbin.org/bytes/1024"; // 1KB random data
    var tempDir = Path.GetTempPath();
    var fileName = $"test_download_{Guid.NewGuid()}.bin";
    var progress = new Mock<IProgress<DownloadProgress>>().Object;
    var token = new CancellationToken();

    var downloadConfig = new DownloadConfiguration();
    var downloadService = new Mock<IDownloadService>().Object;
    var networkClient = new NetworkClient(downloadService, downloadConfig);

    try {
      await networkClient.DownloadFileAsync(
        testUrl,
        tempDir,
        fileName,
        progress,
        token
      );

      var filePath = Path.Combine(tempDir, fileName);
      File.Exists(filePath).ShouldBeTrue();

      var fileInfo = new FileInfo(filePath);
      fileInfo.Length.ShouldBeInRange(800, 1200); // allow some error
    }
    finally {
      var filePath = Path.Combine(tempDir, fileName);
      if (File.Exists(filePath)) {
        File.Delete(filePath);
      }
    }
  }

  [Fact]
  public async Task DownloadFileAsyncUseNoProxyWithNoProgress() {
    var testUrl = "https://httpbin.org/bytes/1024"; // 1KB random data
    var tempDir = Path.GetTempPath();
    var fileName = $"test_download_{Guid.NewGuid()}.bin";
    var token = new CancellationToken();

    var downloadConfig = new DownloadConfiguration();
    var downloadService = new Mock<IDownloadService>().Object;
    var networkClient = new NetworkClient(downloadService, downloadConfig);

    try {
      await networkClient.DownloadFileAsync(
        testUrl,
        tempDir,
        fileName,
        token
      );

      var filePath = Path.Combine(tempDir, fileName);
      File.Exists(filePath).ShouldBeTrue();

      var fileInfo = new FileInfo(filePath);
      fileInfo.Length.ShouldBeInRange(800, 1200); // allow some error
    }
    finally {
      var filePath = Path.Combine(tempDir, fileName);
      if (File.Exists(filePath)) {
        File.Delete(filePath);
      }
    }
  }

  [Fact]
  public async Task DownloadFileAsyncWithInvalidUrlWithProgressThrowsCommandException() {
    var invalidUrl = "https://invalid-domain-that-doesnt-exist-12345.com/file.zip";
    var tempDir = Path.GetTempPath();
    var fileName = $"test_download_{Guid.NewGuid()}.bin";
    var progress = new Mock<IProgress<DownloadProgress>>().Object;
    var token = new CancellationToken();

    var downloadConfig = new DownloadConfiguration();
    var downloadService = new Mock<IDownloadService>().Object;
    var networkClient = new NetworkClient(downloadService, downloadConfig);

    var exception = await Should.ThrowAsync<CommandException>(async () =>
      await networkClient.DownloadFileAsync(
        invalidUrl,
        tempDir,
        fileName,
        progress,
        token
      )
    );
    exception.Message.ShouldContain("Download failed");
  }

  [Fact]
  public async Task DownloadFileAsyncWithInvalidUrlWithNoProgressThrowsCommandException() {
    var invalidUrl = "https://invalid-domain-that-doesnt-exist-12345.com/file.zip";
    var tempDir = Path.GetTempPath();
    var fileName = $"test_download_{Guid.NewGuid()}.bin";
    var token = new CancellationToken();

    var downloadConfig = new DownloadConfiguration();
    var downloadService = new Mock<IDownloadService>().Object;
    var networkClient = new NetworkClient(downloadService, downloadConfig);

    var exception = await Should.ThrowAsync<CommandException>(async () =>
      await networkClient.DownloadFileAsync(
        invalidUrl,
        tempDir,
        fileName,
        token
      )
    );
    exception.Message.ShouldContain("Download failed");
  }

  // a testable network client that exposes protected methods
  private sealed class TestableNetworkClient : NetworkClient {

    public TestableNetworkClient(IDownloadService downloadService, DownloadConfiguration downloadConfiguration)
      : base(downloadService, downloadConfiguration) {
    }

    // public for testing
    public HttpClientHandler PublicCreateHttpClientHandler(string? proxyUrl) => CreateHttpClientHandler(proxyUrl);

    // public for testing
    public IDownload PublicCreateDownloadWithProxy(string url, string destinationDirectory, string filename, string? proxyUrl = null) => CreateDownloadWithProxy(url, destinationDirectory, filename, proxyUrl);
  }
}
