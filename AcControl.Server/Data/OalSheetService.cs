//namespace AcControl.Server.Data;

//using AcControl.Server.Data.Models;
//using Google.Apis.Services;
//using Google.Apis.Sheets.v4;
//using System.Threading;

//public class OalSheetService
//{
//    private const string CAT_FEED_SHEET_NAME = "CatFeeds";
//    private const string CAT_FOOD_SHEET_NAME = "CatFoods";

//    private readonly string mApiKey;
//    private readonly string mSpreadsheetId;
//    private readonly SheetsService mSheetService;

//    public OalSheetService(IConfiguration config)
//    {
//        mApiKey = config.GetValue<string>("Google:ApiKey")!;
//        mSpreadsheetId = config.GetValue<string>("Google:SpreadsheetId")!;

//        mSheetService = new SheetsService(new BaseClientService.Initializer()
//        {
//            ApiKey = mApiKey,
//        });
//    }

//    public async Task Test()
//    {
//        //var client = new GraphServiceClient(mAuthProvider);
//        //var drive = await client.Me.Drive.GetAsync();
        
//        //try
//        //{
//        //    var token = await mTokenAcquisitionService.GetAccessTokenForUserAsync(new string[] { "Files.ReadWrite" });
//        //}
//        //catch
//        //{

//        //}
//    }

//    public async Task<IEnumerable<CatFeedEntry>> GetCatFeeds(CancellationToken cancellationToken)
//    {
//        var spreadsheet = await mSheetService.Spreadsheets
//            .Get(mSpreadsheetId)
//            .ExecuteAsync(cancellationToken);
//        var catFeedsSheet = spreadsheet.Sheets
//            .First(s => s.Properties.Title == CAT_FEED_SHEET_NAME)!;
//        var dataRange = catFeedsSheet.BandedRanges
//            .First().Range;

//        var values = await mSheetService.Spreadsheets.Values.Get(
//            mSpreadsheetId, 
//            $"{CAT_FEED_SHEET_NAME}!R2C1:R{dataRange.EndRowIndex + 1}C{dataRange.EndColumnIndex + 1}")
//            .ExecuteAsync(cancellationToken);

//        return values.Values.Select(CatFeedEntry.From);
//    }

//    public async Task<IEnumerable<CatFood>> GetCatFoods(CancellationToken cancellationToken)
//    {
//        var spreadsheet = await mSheetService.Spreadsheets
//            .Get(mSpreadsheetId)
//            .ExecuteAsync(cancellationToken);
//        var catFeedsSheet = spreadsheet.Sheets
//            .First(s => s.Properties.Title == CAT_FOOD_SHEET_NAME)!;
//        var dataRange = catFeedsSheet.BandedRanges
//            .First().Range;

//        var values = await mSheetService.Spreadsheets.Values.Get(
//            mSpreadsheetId,
//            $"{CAT_FOOD_SHEET_NAME}!R2C1:R{dataRange.EndRowIndex + 1}C{dataRange.EndColumnIndex + 1}")
//            .ExecuteAsync(cancellationToken);

//        return values.Values.Select(CatFood.From);
//    }

//    public async Task<int> AddCatFeed(CatFeedEntry entry, CancellationToken cancellationToken)
//    {
//        var spreadsheet = await mSheetService.Spreadsheets
//            .Get(mSpreadsheetId)
//            .ExecuteAsync(cancellationToken);
//        var catFeedsSheet = spreadsheet.Sheets
//            .First(s => s.Properties.Title == CAT_FEED_SHEET_NAME)!;
//        var dataRange = catFeedsSheet.BandedRanges
//            .First().Range;
        
//        var newData = new Google.Apis.Sheets.v4.Data.ValueRange();
//        newData.Values = new List<IList<object>>() { entry.ToList() };
//        newData.Range = $"{CAT_FEED_SHEET_NAME}!R{dataRange.EndRowIndex + 2}C{dataRange.EndColumnIndex + 1}";
        
//        var values = await mSheetService.Spreadsheets.Values.Update(
//            new Google.Apis.Sheets.v4.Data.ValueRange(),
//            mSpreadsheetId,
//            newData.Range)
//            .ExecuteAsync(cancellationToken);

//        return dataRange.EndRowIndex!.Value + 2;
//    }
//}
