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
  // menu name, and the line under it in the phone menu
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
    ["word-and-deed.html","Word<span class=\"nl\"> and</span><span class=\"ns\"> &amp;</span> Deed","Word and Deed","Preaching and good works together"]
  ];

  // Small helpers pages share
  function esc(s){return String(s).replace(/[&<>"]/g,function(c){return{"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;"}[c]})}
  // A BibleGateway (ESV) link for one reference
  function gw(ref){return "https://www.biblegateway.com/passage/?search="+encodeURIComponent(ref)+"&version=ESV"}
  // "Genesis 15:5; 22:18" -> ["Genesis 15:5","Genesis 22:18"]: carry the book name forward
  function splitRefs(s){var book="";return s.split("; ").map(function(r){var m=/^((?:[1-3] )?[A-Z][a-z]+(?: of [A-Z][a-z]+)?) \d/.exec(r);if(m)book=m[1];else if(book)r=book+" "+r;return r})}

  function curTheme(){return root.dataset.theme||(matchMedia("(prefers-color-scheme: dark)").matches?"dark":"light")}

  // Build the nav into the page's <nav class="sitenav"> and wire it up
  function nav(){
    var el=document.querySelector(".sitenav");if(!el||el.dataset.built)return;el.dataset.built="1";
    var here=location.pathname.split("/").pop()||"index.html";
    var h='<span class="home">Bible History</span>';
    PAGES.forEach(function(p){h+='<a href="'+p[0]+'"'+(p[0]===here?' aria-current="page"':'')+'>'+p[1]+'</a>'});
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
    PAGES.forEach(function(p){var a=document.createElement("a");a.href=p[0];if(p[0]===here)a.setAttribute("aria-current","page");
      a.innerHTML="<b></b><span></span>";a.firstChild.textContent=p[2];a.lastChild.textContent=p[3];menu.appendChild(a)});
    function setMenu(open,focus){menu.hidden=!open;mbtn.setAttribute("aria-expanded",open);if(open&&focus)menu.querySelector("a").focus();if(!open&&focus)mbtn.focus()}
    mbtn.addEventListener("click",function(){setMenu(menu.hidden,true)});
    document.addEventListener("click",function(e){if(!menu.hidden&&!e.target.closest("#siteMenu,#menuBtn"))setMenu(false)});
    document.addEventListener("keydown",function(e){if(e.key==="Escape"&&!menu.hidden){e.stopPropagation();setMenu(false,true)}},true);
    addEventListener("resize",function(){if(!menu.hidden&&getComputedStyle(mbtn).display==="none")setMenu(false)});

    // Anything else that sticks sits below the nav (--navh)
    function size(){root.style.setProperty("--navh",el.offsetHeight+"px")}
    size();if(window.ResizeObserver)new ResizeObserver(size).observe(el);
    // Desktop: if the tabs wrap onto a second line, switch to the compact nav
    function fit(){el.classList.remove("compact");if(getComputedStyle(mbtn).display!=="none")return;
      var tops=[].map.call(el.querySelectorAll(":scope>a,:scope>.home,:scope>.theme-btn"),function(e){return e.offsetTop});
      if(Math.max.apply(null,tops)-Math.min.apply(null,tops)>8)el.classList.add("compact")}
    fit();addEventListener("resize",fit);if(document.fonts)document.fonts.ready.then(fit);
    function stuck(){el.classList.toggle("stuck",scrollY>4)}
    stuck();addEventListener("scroll",stuck,{passive:true});

    // Keep the phone's status bar color in step with the light/dark choice
    function bar(){var t=root.dataset.theme;
      document.querySelectorAll('meta[name="theme-color"]').forEach(function(m){var dark=t?t==="dark":/dark/.test(m.media);m.content=dark?"#10141f":"#eef0f3"})}
    bar();new MutationObserver(bar).observe(root,{attributes:true,attributeFilter:["data-theme"]});
  }

  window.Site={PAGES:PAGES,esc:esc,gw:gw,splitRefs:splitRefs,theme:curTheme,nav:nav};
})();
