using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace Teeth {
class T {
  static int pass=0, fail=0;
  static void Check(string name, bool ok){ if(ok){pass++;Console.WriteLine("PASS  "+name);} else {fail++;Console.WriteLine("FAIL  "+name);} }
  static WorkBlockModel B(string n,string s,string e,int i,int o){ return new WorkBlockModel{Name=n,StartTime=s,EndTime=e,TargetIn=i,TargetOut=o}; }
  static WorkBlockModel Closed(string n){ return B(n,"","",0,0); }
  static string Hm(int m){ m%=1440; return (m/60).ToString("00")+":"+(m%60).ToString("00"); }
  static int Main(){
    var ss = new DateTime(2026,11,20,8,0,0);
    var day = new List<WorkBlockModel>{
      B("Work 1","08:00","10:00",400,390), B("Work 2","10:00","12:00",400,390),
      B("Work 3","12:30","15:00",500,480), B("Work 4","15:00","17:00",400,390),
      Closed("Work 5"),Closed("Work 6"),Closed("Work 7"),Closed("Work 8"),Closed("Work 9"),Closed("Work 10")};

    // ---- 4 open, 6 closed
    Check("4 open + 6 closed validates", ScheduleValidator.Validate("Day Schedule", day).Count==0);
    var end = TargetCalculator.Calculate(ss.AddHours(12), ss, day);
    Check("Plan = 1700/1650 (open works only)", end.PlanIn==1700 && end.PlanOut==1650);
    Check("Target at shift end = Plan", end.TargetIn==1700 && end.TargetOut==1650);
    var mid = TargetCalculator.Calculate(ss.AddHours(1), ss, day);
    Check("Half way Work 1 = 200/195", mid.TargetIn==200 && mid.TargetOut==195);
    var brk = TargetCalculator.Calculate(ss.AddHours(4.25), ss, day);
    Check("Gap 12:00-12:30 holds 800/780", brk.TargetIn==800 && brk.TargetOut==780);
    Check("Before first work starts Target = 0", TargetCalculator.Calculate(ss, ss, day).TargetIn==0);

    // ---- closed row with leftover targets
    var stale = day.ToList(); stale[5]=B("Work 6","","",999,999);
    var r = TargetCalculator.Calculate(ss.AddHours(12), ss, stale);
    Check("Closed row with leftover targets ignored", r.PlanIn==1700 && r.PlanOut==1650);

    // ---- close a MIDDLE work (Work 2 closed, others open)
    var mc = day.ToList(); mc[1]=Closed("Work 2");
    var m1 = TargetCalculator.Calculate(ss.AddHours(12), ss, mc);
    Check("Middle work closed: Plan = 1300/1260", m1.PlanIn==1300 && m1.PlanOut==1260);
    var m2 = TargetCalculator.Calculate(ss.AddHours(3), ss, mc);   // 11:00, inside closed slot
    Check("During a closed work's old slot Target holds at Work 1 (400/390)", m2.TargetIn==400 && m2.TargetOut==390);

    // ---- any number of works: 5, 8, 15
    foreach (int n in new[]{5,8,15}) {
      var list = new List<WorkBlockModel>();
      int slot = 720 / n;                       // minutes per work, gap of 0
      for (int k=0;k<n;k++) list.Add(B("Work "+(k+1), Hm(480+k*slot), Hm(480+(k+1)*slot), 100, 98));
      var errs = ScheduleValidator.Validate("Day Schedule", list);
      var pr = TargetCalculator.Calculate(ss.AddHours(12), ss, list);
      Check(n+" works validate, Plan = "+(n*100)+"/"+(n*98)+", Target(end)=Plan",
            errs.Count==0 && pr.PlanIn==n*100 && pr.PlanOut==n*98 && pr.TargetIn==pr.PlanIn && pr.TargetOut==pr.PlanOut);
    }

    // ---- work added at the bottom in any order: no false overlap, sorted on save
    var ooo = day.ToList(); ooo[4]=B("Work 5","12:00","12:30",50,48);       // fits the gap, listed after Work 4
    Check("Out-of-order row that fits a gap is NOT an overlap", ScheduleValidator.Validate("Day Schedule", ooo).Count==0);
    var norm = ScheduleValidator.Normalize("Day Schedule", ooo);
    Check("Normalize sorts open works by Start time", string.Join(",", norm.Take(5).Select(b=>b.StartTime))=="08:00,10:00,12:00,12:30,15:00");
    Check("Normalize puts closed works last with BlockOrder 0..n-1", norm.Skip(5).All(b=>b.StartTime=="") && norm.Select(b=>b.BlockOrder).SequenceEqual(Enumerable.Range(0,norm.Count)));

    // ---- real overlap (any order)
    var ov = day.ToList(); ov[4]=B("Work 5","11:00","12:30",1,1);
    var e1 = ScheduleValidator.Validate("Day Schedule", ov);
    Check("Real overlap still caught even if row is out of order", e1.Count>=1 && e1.Any(x=>x.Message.Contains("overlaps")));

    // ---- half set / same / outside / negative
    var half = day.ToList(); half[4]=B("Work 5","16:00","",0,0);
    Check("Only Start set -> clear error", ScheduleValidator.Validate("Day Schedule", half).Any(x=>x.RowIndex==4 && x.Message.Contains("only one")));
    var same = day.ToList(); same[4]=B("Work 5","16:00","16:00",10,10);
    Check("Start == End rejected", ScheduleValidator.Validate("Day Schedule", same).Any(x=>x.RowIndex==4));
    Check("Calculator ignores Start == End", TargetCalculator.Calculate(ss.AddHours(12), ss, same).PlanIn==1700);
    var outs = day.ToList(); outs[3]=B("Work 4","15:00","21:00",1,1);
    Check("Past 20:00 rejected on Day", ScheduleValidator.Validate("Day Schedule", outs).Any(x=>x.Message.Contains("outside")));
    var neg = day.ToList(); neg[0]=B("Work 1","08:00","10:00",-5,390);
    Check("Negative target rejected", ScheduleValidator.Validate("Day Schedule", neg).Any(x=>x.Message.Contains("negative")));

    // ---- night across midnight
    var night = new List<WorkBlockModel>{
      B("Work 1","20:00","22:00",400,390), B("Work 2","22:00","23:30",300,290),
      B("Work 3","00:10","02:00",300,290), B("Work 4","02:00","04:00",400,390), Closed("Work 5"),Closed("Work 6")};
    Check("Night 4 works across midnight validates", ScheduleValidator.Validate("Night Schedule", night).Count==0);
    var ns = new DateTime(2026,11,20,20,0,0);
    var nr = TargetCalculator.Calculate(ns.AddHours(12), ns, night);
    Check("Night plan 1400/1360, Target(end)=Plan", nr.PlanIn==1400 && nr.PlanOut==1360 && nr.TargetIn==1400 && nr.TargetOut==1360);
    var nmid = TargetCalculator.Calculate(new DateTime(2026,11,21,0,0,0), ns, night);   // 00:00, in the gap
    Check("Night: 23:30-00:10 gap holds 700/680", nmid.TargetIn==700 && nmid.TargetOut==680);
    var nn = night.ToList(); nn[1]=B("Work 2","19:00","23:00",1,1);
    Check("Error carries its profile", ScheduleValidator.Validate("Night Schedule", nn)[0].Profile=="Night Schedule");

    // ---- everything closed
    var none = new List<WorkBlockModel>{Closed("Work 1"),Closed("Work 2")};
    var nz = TargetCalculator.Calculate(ss.AddHours(3), ss, none);
    Check("All closed -> 0, no crash", nz.PlanIn==0 && nz.TargetOut==0);

    // ---- property test: random schedules
    var rnd = new Random(12345); int bad=0;
    for (int it=0; it<2000; it++) {
      var list = new List<WorkBlockModel>(); int cur = rnd.Next(0,60); int count = rnd.Next(1,16); int planIn=0, planOut=0;
      for (int k=0;k<count;k++) {
        int len = rnd.Next(5,70); if (cur+len>720) break;
        bool open = rnd.Next(4)!=0; int ti=rnd.Next(1,500), to=rnd.Next(1,500);
        if (open) { list.Add(B("W"+k,Hm(480+cur),Hm(480+cur+len),ti,to)); planIn+=ti; planOut+=to; }
        else list.Add(Closed("W"+k));
        cur += len + rnd.Next(0,25);
      }
      if (list.Count==0) continue;
      var sh = list.OrderBy(x=>rnd.Next()).ToList();   // shuffled row order
      if (ScheduleValidator.Validate("Day Schedule", sh).Count!=0) { bad++; continue; }
      var e = TargetCalculator.Calculate(ss.AddHours(12), ss, sh);
      if (e.PlanIn!=planIn || e.PlanOut!=planOut) { bad++; continue; }
      int prevI=-1, prevO=-1;
      for (int mnt=0; mnt<=720; mnt+=7) {
        var t = TargetCalculator.Calculate(ss.AddMinutes(mnt), ss, sh);
        if (t.TargetIn<prevI || t.TargetOut<prevO || t.TargetIn>planIn+1 || t.TargetOut>planOut+1) { bad++; break; }
        prevI=t.TargetIn; prevO=t.TargetOut;
      }
      if (Math.Abs(e.TargetIn-planIn)>count || Math.Abs(e.TargetOut-planOut)>count) bad++;
    }
    Check("2000 random schedules (any count, shuffled, some closed): valid, Plan exact, Target never goes down, ends at Plan", bad==0);

    // ---- database round trip (save -> reload -> Target), like the real Save button
    Directory.CreateDirectory(AppBootstrap.AppFilesDir);
    if (File.Exists(AppBootstrap.DbFile)) File.Delete(AppBootstrap.DbFile);
    var db = new DatabaseManager(); db.InitializeDatabase();
    var toSave = ScheduleValidator.Normalize("Day Schedule", ooo);          // 5 open + 5 closed
    db.SaveWorkBlocks("Day Schedule", toSave);
    var back = db.LoadWorkBlocks("Day Schedule");
    var br = TargetCalculator.Calculate(ss.AddHours(12), ss, back);
    Check("DB: 5 open + 5 closed saved and reloaded, Plan 1750/1698", back.Count==10 && br.PlanIn==1750 && br.PlanOut==1698);
    var more = new List<WorkBlockModel>();
    for (int k=0;k<15;k++) more.Add(B("Work "+(k+1), Hm(480+k*48), Hm(480+(k+1)*48), 10, 9));
    db.SaveWorkBlocks("Day Schedule", ScheduleValidator.Normalize("Day Schedule", more));
    var back2 = db.LoadWorkBlocks("Day Schedule");
    var br2 = TargetCalculator.Calculate(ss.AddHours(12), ss, back2);
    Check("DB: re-save with 15 works replaces old list, Plan 150/135", back2.Count==15 && br2.PlanIn==150 && br2.PlanOut==135);
    var two = new List<WorkBlockModel>{ B("Work 1","08:00","09:00",233,230), Closed("Work 2") };
    db.SaveWorkBlocks("Day Schedule", ScheduleValidator.Normalize("Day Schedule", two));
    var back3 = db.LoadWorkBlocks("Day Schedule");
    Check("DB: re-save with only 1 open work + 1 closed", back3.Count==2 && TargetCalculator.Calculate(ss.AddHours(12), ss, back3).PlanIn==233);
    db.SaveWorkBlocks("Night Schedule", ScheduleValidator.Normalize("Night Schedule", night));
    Check("DB: Day and Night stay separate", db.LoadWorkBlocks("Night Schedule").Count==6 && db.LoadWorkBlocks("Day Schedule").Count==2);

    Console.WriteLine("\n"+pass+" passed, "+fail+" failed");
    return fail==0?0:1;
  }
}}
