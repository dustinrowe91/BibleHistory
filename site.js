// Shared by every page. Load it in <head> (not deferred) so the saved light/dark choice applies before the page paints.
// Each page puts <nav class="sitenav" aria-label="Site"></nav><script>Site.nav()</script> where the nav goes.
// To add a page to the site: add one line to PAGES below, and add the file to CORE in sw.js.
(function(){
  var root=document.documentElement;

  // Saved theme (light/dark), if the reader picked one
  try{var saved=localStorage.getItem("theme");if(saved)root.dataset.theme=saved}catch(e){}

  // Offline support (sw.js)
  if("serviceWorker" in navigator&&location.protocol.indexOf("http")===0)
    addEventListener("load",function(){navigator.serviceWorker.register("sw.js").catch(function(){})});

  // Every page in the nav, in order: file, tab label (HTML: .nl is hidden in the compact nav, .ns shown only there),
  // menu name, the line under it in the phone menu, and whether it needs the server (shown only when the site is
  // served by the ASP.NET Core app, which answers /api/me; the GitHub Pages copy has no server)
  var PAGES=[
    ["index.html","Timeline","Timeline","The story of Scripture, Creation to today"],
    ["books.html","Books","Books","All 66 books on one timeline"],
    ["kings.html","Kings<span class=\"nl\"> &amp; Prophets</span>","Kings & Prophets","Every king of Israel and Judah"],
    ["people.html","People","People","The line of Jesus, Adam to Joseph"],
    ["covenants.html","Covenants","Covenants","From Adam to the New Covenant"],
    ["prophecy.html","Prophecy","Prophecy","Promises fulfilled in Christ"],
    ["plagues.html","Plagues","Plagues","The plagues of Egypt and what they mean"],
    ["church.html","Churches","Churches","Denominations, sects, and world religions"],
    ["maps.html","Maps","Maps","Places and journeys of the Bible"],
    ["word-and-deed.html","Word<span class=\"nl\"> and</span><span class=\"ns\"> &amp;</span> Deed","Word and Deed","Preaching and good works together"],
    ["ask.html","Ask","Ask","Ask a question about the Bible",true]
  ];

  // Small helpers pages share
  function esc(s){return String(s).replace(/[&<>"]/g,function(c){return{"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;"}[c]})}
  // A BibleGateway (ESV) link for one reference
  function gw(ref){return "https://www.biblegateway.com/passage/?search="+encodeURIComponent(ref)+"&version=ESV"}
  // "Genesis 15:5; 22:18" -> ["Genesis 15:5","Genesis 22:18"]: carry the book name forward
  function splitRefs(s){var book="";return s.split("; ").map(function(r){var m=/^((?:[1-3] )?[A-Z][a-z]+(?: of [A-Z][a-z]+)?) \d/.exec(r);if(m)book=m[1];else if(book)r=book+" "+r;return r})}

  // Scripture references in running text, such as "Romans 8:28" or "Genesis 12:1-3; 15:6"
  var BOOK="(?:[1-3] )?(?:Genesis|Exodus|Leviticus|Numbers|Deuteronomy|Joshua|Judges|Ruth|Samuel|Kings|Chronicles|Ezra|Nehemiah|Esther|Job|Psalms?|Proverbs|Ecclesiastes|Song of (?:Songs|Solomon)|Isaiah|Jeremiah|Lamentations|Ezekiel|Daniel|Hosea|Joel|Amos|Obadiah|Jonah|Micah|Nahum|Habakkuk|Zephaniah|Haggai|Zechariah|Malachi|Matthew|Mark|Luke|John|Acts|Romans|Corinthians|Galatians|Ephesians|Philippians|Colossians|Thessalonians|Timothy|Titus|Philemon|Hebrews|James|Peter|Jude|Revelation)";
  var VV=":\\d+[a-c]?(?:[-–]\\d+(?::\\d+)?)?(?:, ?\\d+(?:[-–]\\d+)?)*";   // ":1-3", ":5, 9", ":28-9:2"
  // Book chapter[:verses], then any "; [Book] chapter:verses" that follow
  var REF_RE=new RegExp("\\b"+BOOK+" \\d+(?:"+VV+")?(?:; (?:"+BOOK+" )?\\d+"+VV+")*","g");
  // Turn already-escaped text into text with each reference linked to BibleGateway
  function linkRefs(html){
    return html.replace(REF_RE,function(m){
      var parts=m.split("; "),full=splitRefs(m.replace(/–/g,"-"));
      return parts.map(function(p,i){return '<a class="ref" href="'+esc(gw(full[i]))+'" target="_blank" rel="noopener">'+p+'</a>'}).join("; ");
    });
  }
  // A small, safe Markdown renderer for Ask answers: paragraphs, ### headings, - and 1. lists, **bold**, *italic*,
  // links to pages on this site only, and Scripture references linked. Everything else is shown as plain text.
  function inline(t){
    var out="",last=0,re=/\[([^\]\n]+)\]\(([^)\s]+)\)/g,m;
    function plain(s){
      s=esc(s).replace(/\*\*([^*\n]+)\*\*/g,"<strong>$1</strong>").replace(/(^|[^*\w])\*([^*\n]+)\*(?![*\w])/g,"$1<em>$2</em>");
      return linkRefs(s);
    }
    while((m=re.exec(t))){
      out+=plain(t.slice(last,m.index));
      var ok=/^[a-z0-9-]+\.html(?:#[\w-]*)?$/.test(m[2]);
      out+=ok?'<a href="'+esc(m[2])+'">'+esc(m[1])+'</a>':plain(m[1]);
      last=re.lastIndex;
    }
    return out+plain(t.slice(last));
  }
  function markdown(src){
    var lines=String(src).replace(/\r/g,"").split("\n"),html="",para=[],list=null;
    function flushPara(){if(para.length){html+="<p>"+inline(para.join(" "))+"</p>";para=[]}}
    function flushList(){if(list){html+="</"+list+">";list=null}}
    lines.forEach(function(line){
      var h=/^#{1,4}\s+(.*)$/.exec(line),ul=/^\s*[-*]\s+(.*)$/.exec(line),ol=/^\s*\d+[.)]\s+(.*)$/.exec(line);
      if(!line.trim()){flushPara();flushList();return}
      if(h){flushPara();flushList();html+="<h3>"+inline(h[1])+"</h3>";return}
      if(ul||ol){flushPara();var k=ul?"ul":"ol";if(list!==k){flushList();html+="<"+k+">";list=k}html+="<li>"+inline((ul||ol)[1])+"</li>";return}
      flushList();para.push(line.trim());
    });
    flushPara();flushList();
    return html;
  }

  function curTheme(){return root.dataset.theme||(matchMedia("(prefers-color-scheme: dark)").matches?"dark":"light")}

  // Who is signed in, from the server; null where there is no server (the GitHub Pages copy, or offline)
  var mePromise=null;
  function me(fresh){
    if(fresh||!mePromise){
      mePromise=location.protocol.indexOf("http")!==0?Promise.resolve(null):
        fetch("api/me",{credentials:"same-origin",cache:"no-store"})
          .then(function(r){return r.ok&&/json/.test(r.headers.get("content-type")||"")?r.json():null})
          .catch(function(){return null});
    }
    return mePromise;
  }
  function signOut(){
    return fetch("signout",{method:"POST",credentials:"same-origin",headers:{"Content-Type":"application/json"},body:"{}"})
      .then(function(){location.reload()});
  }

  // Build the nav into the page's <nav class="sitenav"> and wire it up
  function nav(){
    var el=document.querySelector(".sitenav");if(!el||el.dataset.built)return;el.dataset.built="1";
    var here=location.pathname.split("/").pop()||"index.html";
    function tab(p){return '<a href="'+p[0]+'"'+(p[0]===here?' aria-current="page"':'')+(p[4]?' data-server':'')+'>'+p[1]+'</a>'}
    var h='<span class="home"><span class="nt">The </span>Scarlet Thread</span>';
    PAGES.forEach(function(p){if(!p[4])h+=tab(p)});
    h+='<button type="button" class="menu-btn" id="menuBtn" aria-expanded="false" aria-controls="siteMenu"><span aria-hidden="true">☰</span> Menu</button>'+
       '<button type="button" class="theme-btn" id="themeBtn"></button><div class="site-menu" id="siteMenu" hidden></div>';
    el.innerHTML=h;

    // Light/dark button
    var themeBtn=document.getElementById("themeBtn");
    function syncTheme(){var d=curTheme()==="dark";
      themeBtn.innerHTML=d?'<span aria-hidden="true">☀</span><span class="tl"> Light</span>':'<span aria-hidden="true">☾</span><span class="tl"> Dark</span>';
      themeBtn.setAttribute("aria-label","Switch to "+(d?"light":"dark")+" theme")}
    themeBtn.addEventListener("click",function(){var t=curTheme()==="dark"?"light":"dark";root.dataset.theme=t;try{localStorage.setItem("theme",t)}catch(e){}syncTheme()});
    syncTheme();

    // Phone menu: every page with a line about it, opened from the Menu button
    var menu=document.getElementById("siteMenu"),mbtn=document.getElementById("menuBtn");
    function menuItem(p){var a=document.createElement("a");a.href=p[0];if(p[0]===here)a.setAttribute("aria-current","page");
      a.innerHTML="<b></b><span></span>";a.firstChild.textContent=p[2];a.lastChild.textContent=p[3];return a}
    PAGES.forEach(function(p){if(!p[4])menu.appendChild(menuItem(p))});
    function setMenu(open,focus){menu.hidden=!open;mbtn.setAttribute("aria-expanded",open);if(open&&focus)menu.querySelector("a").focus();if(!open&&focus)mbtn.focus()}
    mbtn.addEventListener("click",function(){setMenu(menu.hidden,true)});
    document.addEventListener("click",function(e){if(!menu.hidden&&!e.target.closest("#siteMenu,#menuBtn"))setMenu(false)});
    document.addEventListener("keydown",function(e){if(e.key==="Escape"&&!menu.hidden){e.stopPropagation();setMenu(false,true)}},true);
    addEventListener("resize",function(){if(!menu.hidden&&getComputedStyle(mbtn).display==="none")setMenu(false)});

    // Anything else that sticks sits below the nav (--navh)
    function size(){root.style.setProperty("--navh",el.offsetHeight+"px")}
    size();if(window.ResizeObserver)new ResizeObserver(size).observe(el);
    // Desktop: if the tabs wrap onto a second line, switch to the compact nav
    // and if even that wraps, use the Menu button as on a phone
    function wraps(){var tops=[].map.call(el.querySelectorAll(":scope>a,:scope>.home,:scope>.theme-btn,:scope>.acct"),function(e){return e.offsetParent?e.offsetTop:null}).filter(function(t){return t!==null});
      return Math.max.apply(null,tops)-Math.min.apply(null,tops)>8}
    function fit(){el.classList.remove("compact","tight");if(getComputedStyle(mbtn).display!=="none")return;
      if(wraps()){el.classList.add("compact");if(wraps())el.classList.add("tight")}}
    fit();addEventListener("resize",fit);if(document.fonts)document.fonts.ready.then(fit);
    function stuck(){el.classList.toggle("stuck",scrollY>4)}
    stuck();addEventListener("scroll",stuck,{passive:true});

    // Keep the phone's status bar color in step with the light/dark choice
    function bar(){var t=root.dataset.theme;
      document.querySelectorAll('meta[name="theme-color"]').forEach(function(m){var dark=t?t==="dark":/dark/.test(m.media);m.content=dark?"#10141f":"#eef0f3"})}
    bar();new MutationObserver(bar).observe(root,{attributes:true,attributeFilter:["data-theme"]});

    // With a server: the Ask tab, and Sign in or the account menu
    me().then(function(u){
      if(!u)return;
      PAGES.forEach(function(p){if(!p[4])return;
        var t=document.createElement("span");t.innerHTML=tab(p);mbtn.before(t.firstChild);menu.appendChild(menuItem(p))});
      var acct=document.createElement("div");acct.className="acct";
      if(!u.signedIn){
        if(!u.signInReady)return;
        acct.innerHTML='<a class="acct-btn" href="signin?returnUrl='+encodeURIComponent(location.pathname+location.search+location.hash)+'">Sign in</a>';
      }else{
        var first=(u.name||u.email||"?").split(" ")[0];
        acct.innerHTML='<button type="button" class="acct-btn" aria-expanded="false" aria-haspopup="true"><span class="av" aria-hidden="true"></span><span class="an"></span></button>'+
          '<div class="acct-menu" hidden><div class="who"><b></b><span></span></div>'+(u.isAdmin?'<a href="admin.html">Admin</a>':'')+'<button type="button" class="out">Sign out</button></div>';
        var btn=acct.querySelector(".acct-btn"),am=acct.querySelector(".acct-menu");
        acct.querySelector(".av").textContent=first.charAt(0).toUpperCase();
        acct.querySelector(".an").textContent=first;
        btn.setAttribute("aria-label","Account: "+(u.name||u.email));
        acct.querySelector(".who b").textContent=u.name||"";acct.querySelector(".who span").textContent=u.email||"";
        function setAcct(open){am.hidden=!open;btn.setAttribute("aria-expanded",open)}
        btn.addEventListener("click",function(){setAcct(am.hidden)});
        document.addEventListener("click",function(e){if(!am.hidden&&!e.target.closest(".acct"))setAcct(false)});
        document.addEventListener("keydown",function(e){if(e.key==="Escape"&&!am.hidden){setAcct(false);btn.focus()}});
        acct.querySelector(".out").addEventListener("click",signOut);
      }
      themeBtn.after(acct);
      fit();size();
    });
  }

  window.Site={PAGES:PAGES,esc:esc,gw:gw,splitRefs:splitRefs,linkRefs:linkRefs,markdown:markdown,theme:curTheme,me:me,signOut:signOut,nav:nav};
})();
