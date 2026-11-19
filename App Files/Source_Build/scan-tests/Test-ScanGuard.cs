using System; using System.IO;
namespace Teeth {
 static class T {
  static int fails=0;
  static void Eq(object a, object b, string n){ bool ok=Equals(a,b); Console.WriteLine((ok?"PASS ":"FAIL ")+n+" -> "+a); if(!ok) fails++; }
  static int Main(){
   Directory.CreateDirectory(AppBootstrap.AppFilesDir);
   if(File.Exists(AppBootstrap.DbFile)) File.Delete(AppBootstrap.DbFile);
   var db=new DatabaseManager(); db.InitializeDatabase();
   var g=new ScanGuard(db);
   DateTime t0=new DateTime(2026,11,19,9,0,0); string day="2026-11-19 08:00", night="2026-11-19 20:00";
   int outCount=0;
   Func<string,string,DateTime,ScanVerdict> scan=(code,sk,now)=>{ var r=g.Check(code,sk,now); if(r.Verdict==ScanVerdict.Accept){outCount++; g.Remember(r.Code,sk,now);} return r.Verdict; };
   Eq(scan("SN-0001",day,t0),ScanVerdict.Accept,"first SN-0001");
   Eq(scan("SN-0001",day,t0.AddMinutes(5)),ScanVerdict.Duplicate,"SN-0001 again");
   Eq(scan("sn-0001 ",day,t0.AddMinutes(6)),ScanVerdict.Duplicate,"lower case + space still duplicate");
   Eq(scan("SN-0002",day,t0.AddMinutes(7)),ScanVerdict.Accept,"different SN-0002");
   Eq(scan("SN-0001",day,t0.AddHours(2)),ScanVerdict.Duplicate,"SN-0001 two hours later");
   Eq(outCount,2,"OUT count = 2");
   Eq(scan("",day,t0).ToString(),"Unreadable","empty text");
   // survives restart: new DatabaseManager + new guard
   var g2=new ScanGuard(new DatabaseManager());
   Eq(g2.Check("SN-0002",day,t0.AddHours(3)).Verdict,ScanVerdict.Duplicate,"after restart still duplicate");
   // next shift (scope=shift): allowed again
   Eq(g2.Check("SN-0001",night,t0.AddHours(11)).Verdict,ScanVerdict.Accept,"next shift may count again (scope shift)");
   LinkSettings.DuplicateScope="always";
   Eq(g2.Check("SN-0001",night,t0.AddHours(11)).Verdict,ScanVerdict.Duplicate,"scope always blocks next shift");
   LinkSettings.DuplicateScope="day";
   Eq(g2.Check("SN-0001",night,t0.AddHours(11)).Verdict,ScanVerdict.Duplicate,"scope day blocks same day");
   Eq(g2.Check("SN-0001","2026-11-20 08:00",t0.AddDays(1)).Verdict,ScanVerdict.Accept,"scope day allows next day");
   LinkSettings.DuplicateScope="shift";
   // valid list
   File.WriteAllLines(Path.Combine(AppBootstrap.AppFilesDir,"valid.csv"),new[]{"serial,name","SN-0100,a","\"SN-0101\",b"});
   LinkSettings.ValidCodesFile="valid.csv";
   Eq(g2.Check("SN-0100",day,t0).Verdict,ScanVerdict.Accept,"on list");
   Eq(g2.Check("SN-9999",day,t0).Verdict,ScanVerdict.Unknown,"not on list");
   Eq(g2.Check("sn-0101",day,t0).Verdict,ScanVerdict.Accept,"quoted csv, case-insens");
   LinkSettings.ValidCodesFile="missing.csv";
   Eq(g2.Check("SN-9999",day,t0).Verdict,ScanVerdict.Accept,"missing list file = fail open");
   LinkSettings.ValidCodesFile="";
   // reset
   g2.ForgetAll();
   Eq(g2.Check("SN-0002",day,t0).Verdict,ScanVerdict.Accept,"after Reset codes forgotten");
   Console.WriteLine(fails==0?"ALL PASSED":"FAILURES: "+fails); return fails;
  }}}
