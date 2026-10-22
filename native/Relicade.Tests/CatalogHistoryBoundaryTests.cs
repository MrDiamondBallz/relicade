using System.Net;
using System.Text.Json;
using Relicade.Models;
using Relicade.Services;
using Relicade.ViewModels;
namespace Relicade.Tests;
public sealed class CatalogHistoryBoundaryTests : IDisposable
{
 readonly string root = Path.Combine(Path.GetTempPath(),"Relicade.HistoryBoundary",Guid.NewGuid().ToString("N"));
 public void Dispose()=>TestFixtures.CleanupDirectory(root);
 sealed class Transport(Func<HttpRequestMessage,HttpResponseMessage> fn):HttpMessageHandler
 { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken t)=>Task.FromResult(fn(r)); }
 static HttpResponseMessage Json(object v)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(v))};
 static object Page(object[] rows,string? next=null)=>new{items=rows,isDone=next==null,nextCursor=next};
 static object Row(string tag,string state="verified",string? id=null)=>new{version=tag,releaseId=id,state,reasons=Array.Empty<string>(),assets=new[]{new{filename="game.zip",url="https://assets.example.test/game.zip",checksum="sha256:"+new string('a',64)}}};
 static HttpResponseMessage? Metadata(HttpRequestMessage r)=>r.RequestUri!.AbsolutePath switch
 {
 "/api/v1/release-status"=>Json(Page([new{id="id-alpha",slug="alpha",provider="github",repository="fixture/alpha",verified=new{version="v1"}}])),
 "/api/v1/apps"=>Json(Page([new{id="id-alpha",slug="alpha",name="Alpha",launcher=new{folderName="Alpha"}}])),
 _=>null
 };
 static GameInfo App()=>new(){Name="Alpha",FolderName="Alpha",Repository="fixture/alpha",RepositorySource="github",CatalogEntryId="id-alpha"};
 [Theory]
 [InlineData("absent")][InlineData("same")][InlineData("distinct")][InlineData("single")]
 public async Task Distinct_aliases_remain_ambiguous_even_with_equal_metadata(string ids)
 {
  object[] rows=ids=="single"?[Row("v1")]:[Row("v1",id:ids=="absent"?null:"first"),Row("1",id:ids=="absent"?null:ids=="same"?"first":"second")];
  using var h=new Transport(r=>Metadata(r)??Json(Page(rows)));using var http=new HttpClient(h);
  var s=new CatalogReleases(new QuiverCatalogClient(http,"https://catalog.example.test/api/v1"),root);var a=App();
  var c=await s.CheckAsync(a,"01",TestContext.Current.CancellationToken);
  Assert.Equal(ids=="single"?ReleaseCheckState.Verified:ReleaseCheckState.Unverified,c!.State);
  if(ids!="single")Assert.Empty(c.Checksums);
  a.PreferredVersion="01";var chosen=await s.SelectAsync(a,TestContext.Current.CancellationToken);
  if(ids=="single")Assert.Equal("v1",chosen!.tag_name);else Assert.Null(chosen);
 }
 [Theory]
 [InlineData(404,false)][InlineData(410,false)][InlineData(503,true)]
 public async Task Catalog_details_preserves_permanent_failure_non_retry_policy(int code,bool retry)
 {
  var calls=0;
  using var h=new Transport(r=>{
   if(r.RequestUri!.AbsolutePath.EndsWith("/release-history")){calls++;return new((HttpStatusCode)code){Content=new StringContent("synthetic failure")};}
   if(r.RequestUri.AbsolutePath.EndsWith("/reviews"))return Json(Page([]));
   return Json(new{slug="alpha",name="Alpha"});
  });using var http=new HttpClient(h);
  using var m=new BrowseDetailsViewModel(new QuiverCatalogClient(http,"https://catalog.example.test/api/v1"),(_,_)=>App(),(_,_)=>Task.FromResult(new DocumentContent("Synthetic README")));
  await m.OpenAsync(BrowseItem.FromCatalog(new(){Id="id-alpha",Slug="alpha",Name="Alpha"},new Dictionary<string,string>()),TestContext.Current.CancellationToken);
  await m.LoadReleasesAsync(TestContext.Current.CancellationToken);
  Assert.Equal(retry,m.CanRetryReleases);
  await m.LoadReleasesAsync(TestContext.Current.CancellationToken);Assert.Equal(retry?2:1,calls);
 }
 [Theory]
 [InlineData(false)][InlineData(true)]
 public async Task Well_formed_block_is_not_lost_because_another_row_is_malformed(bool malformedFirst)
 {
  var calls=0;
  using var h=new Transport(r=>{
   if(Metadata(r) is{} meta)return meta;
   if(++calls==1){object bad=new{version=123,state="verified"};object block=Row("v1","blocked");return Json(Page(malformedFirst?[bad,block]:[block,bad]));}
   throw new HttpRequestException("Synthetic offline retry");
  });using var http=new HttpClient(h);
  var s=new CatalogReleases(new QuiverCatalogClient(http,"https://catalog.example.test/api/v1"),root);var a=App();
  var first=await s.CheckAsync(a,"v1",TestContext.Current.CancellationToken);Assert.NotEqual(ReleaseCheckState.Verified,first!.State);
  var retry=await s.CheckAsync(a,"v1",TestContext.Current.CancellationToken);Assert.Equal(ReleaseCheckState.Blocked,retry!.State);Assert.Null(s.Cached(a));
 }
 [Fact]
 public async Task Many_advancing_empty_pages_are_not_truncated()
 {
  var count=0;using var h=new Transport(r=>{var i=count++;Assert.Equal("?limit=100"+(i==0?"":"&cursor=p"+i),r.RequestUri!.Query);return Json(Page(i==153?[Row("v1","blocked")]:[],i==153?null:"p"+(i+1)));});using var http=new HttpClient(h);
  var p=await new QuiverCatalogClient(http,"https://catalog.example.test/api/v1").GetReleaseHistoryAsync("alpha",TestContext.Current.CancellationToken);
  Assert.Equal(154,count);Assert.True(p.IsDone);Assert.Equal("blocked",Assert.Single(p.Items).State);
 }
}
