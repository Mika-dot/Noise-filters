(() => {
  'use strict';
  const $ = id => document.getElementById(id);
  const N = 400;
  let seed = 42017;
  let state = { clean: [], noisy: [], spikes: [] };

  const meta = {
    moving: { name:'Скользящее среднее', formula:'y[i] = Σ x[k] / N', best:'Белый случайный шум, медленные датчики', trade:'Размывает фронты и добавляет задержку', fn:(x,w)=>centered(x,w,(a)=>mean(a)) },
    ema: { name:'EMA', formula:'y[i] = α·x[i] + (1−α)·y[i−1]', best:'Потоковая обработка и микроконтроллеры', trade:'Малый α заметно запаздывает', strength:'Коэффициент α', fn:(x,w,s)=>ema(x,s) },
    doubleEma: { name:'Double EMA', formula:'DEMA = 2·EMA(x) − EMA(EMA(x))', best:'Сглаживание тренда с меньшим лагом', trade:'Может переоценивать быстрые переходы', strength:'Коэффициент α', fn:(x,w,s)=>{const a=ema(x,s),b=ema(a,s);return a.map((v,i)=>2*v-b[i])} },
    median: { name:'Медианный', formula:'y[i] = median(window(x, i))', best:'Импульсные помехи и одиночные пики', trade:'Ступенчатый результат на плавных данных', fn:(x,w)=>centered(x,w,median) },
    hampel: { name:'Hampel', formula:'|x−median| > t·1.4826·MAD → median', best:'Точечные аномалии при сохранении нормальных данных', trade:'Не сглаживает обычный гауссов шум', strength:'Порог MAD', strengthMap:v=>1+v*5, fn:(x,w,s)=>hampel(x,w,s) },
    gaussian: { name:'Gaussian', formula:'w[k] = exp(−k² / 2σ²)', best:'Высокочастотный шум без резких выбросов', trade:'Сглаживает узкие пики полезного сигнала', strength:'Sigma σ', strengthMap:v=>.4+v*4, fn:(x,w,s)=>gaussian(x,w,s) },
    savgol: { name:'Savitzky–Golay', formula:'локальный МНК-полином → значение в центре', best:'Сохранение формы пиков и производных', trade:'Плохо переносит сильные одиночные выбросы', fn:(x,w)=>savitzkyGolay(x,w,Math.min(3,w-1)) },
    bilateral: { name:'Bilateral 1D', formula:'w = Gaussian(distance) · Gaussian(Δvalue)', best:'Шум при необходимости сохранить скачки', trade:'Дороже среднего и чувствителен к σ', strength:'Range σ', strengthMap:v=>2+v*35, fn:(x,w,s)=>bilateral(x,w,Math.max(1,w/4),s) },
    oneEuro: { name:'One Euro', formula:'cutoff = minCutoff + β·|dx/dt|', best:'Координаты, жесты и быстро меняющиеся датчики', trade:'Нужно подобрать частоту и β', strength:'Адаптивность β', strengthMap:v=>v*.12, fn:(x,w,s)=>oneEuro(x,50,1,s) },
    kalman: { name:'Скалярный Kalman', formula:'K=P/(P+R); x̂←x̂+K(z−x̂)', best:'Измерения с известной дисперсией шума', trade:'Слабая модель не предсказывает сложную динамику', strength:'Process noise Q', strengthMap:v=>.01+v*2, fn:(x,w,s)=>kalman(x,36,s) },
    rc: { name:'RC Low-pass', formula:'α=dt/(RC+dt); y←y+α(x−y)', best:'Онлайн-фильтрация физических датчиков', trade:'Срез частоты задаёт компромисс шум/лаг', strength:'Частота среза', strengthMap:v=>.2+v*10, fn:(x,w,s)=>lowPass(x,s,50) },
    deadband: { name:'Deadband', formula:'|x−last| < width → last', best:'Дребезг вокруг уставки и снижение трафика', trade:'Создаёт ступени и скрывает малые изменения', strength:'Ширина зоны', strengthMap:v=>v*18, fn:(x,w,s)=>deadband(x,s) },
    slew: { name:'Slew-rate limiter', formula:'Δy = clamp(x−y, −fall, rise)', best:'Ограничение физически невозможных скачков', trade:'Преднамеренно замедляет быстрый переход', strength:'Макс. шаг', strengthMap:v=>.5+v*14, fn:(x,w,s)=>slew(x,s) }
  };

  function initSelectors(){
    Object.entries(meta).forEach(([key,value])=>{
      $('filter').add(new Option(value.name,key));
      $('compare').add(new Option(value.name,key));
    });
    $('filter').value='hampel'; $('compare').value='gaussian';
  }
  function mulberry32(a){return()=>{a|=0;a=a+0x6D2B79F5|0;let t=Math.imul(a^a>>>15,1|a);t=t+Math.imul(t^t>>>7,61|t)^t;return((t^t>>>14)>>>0)/4294967296}}
  function gaussianRandom(random){const u=1-random(),v=1-random();return Math.sqrt(-2*Math.log(u))*Math.cos(2*Math.PI*v)}
  function cleanSignal(type){return Array.from({length:N},(_,i)=>{const t=i/(N-1);switch(type){case'sine':return 52*Math.sin(t*Math.PI*4);case'step':return t<.48?-25:42;case'ramp':return -55+115*t;case'pulse':return t>.34&&t<.64?52:-28;case'triangle':return 55*(1-4*Math.abs(Math.round(t-.25)-(t-.25)));default:return 28*Math.sin(t*Math.PI*5)+(t>.52?32:0)+(t>.77?-18:0)}})}
  function regenerate(){
    const random=mulberry32(seed), clean=cleanSignal($('signal').value), amplitude=+$('noise').value, chance=+$('spikes').value/100, spikes=[];
    const noisy=clean.map((v,i)=>{let value=v+gaussianRandom(random)*amplitude*.42;if(random()<chance){value+=(random()<.5?-1:1)*(35+random()*45);spikes.push(i)}return value});
    state={clean,noisy,spikes}; update();
  }
  function mirror(i,n){while(i<0||i>=n)i=i<0?-i:2*n-i-2;return i}
  function windowAt(x,i,w){const r=Math.floor(w/2);return Array.from({length:w},(_,k)=>x[mirror(i+k-r,x.length)])}
  function mean(a){return a.reduce((s,v)=>s+v,0)/a.length}
  function median(a){const b=[...a].sort((x,y)=>x-y),m=b.length>>1;return b.length%2?b[m]:(b[m-1]+b[m])/2}
  function centered(x,w,aggregate){return x.map((_,i)=>aggregate(windowAt(x,i,w)))}
  function ema(x,a){if(!x.length)return[];let y=x[0];return x.map((v,i)=>i?(y=a*v+(1-a)*y):y)}
  function hampel(x,w,t){return x.map((v,i)=>{const a=windowAt(x,i,w),m=median(a),mad=median(a.map(n=>Math.abs(n-m))),limit=Math.max(1e-9,t*1.4826*mad);return Math.abs(v-m)>limit?m:v})}
  function gaussian(x,w,sigma){const r=w>>1,weights=Array.from({length:w},(_,i)=>Math.exp(-((i-r)**2)/(2*sigma*sigma))),total=weights.reduce((a,b)=>a+b);return x.map((_,i)=>weights.reduce((a,b,k)=>a+b*x[mirror(i+k-r,x.length)],0)/total)}
  function invert(a){const n=a.length,m=a.map((row,i)=>[...row,...Array.from({length:n},(_,j)=>i===j?1:0)]);for(let p=0;p<n;p++){let best=p;for(let r=p+1;r<n;r++)if(Math.abs(m[r][p])>Math.abs(m[best][p]))best=r;[m[p],m[best]]=[m[best],m[p]];const d=m[p][p];for(let c=0;c<2*n;c++)m[p][c]/=d;for(let r=0;r<n;r++){if(r===p)continue;const f=m[r][p];for(let c=0;c<2*n;c++)m[r][c]-=f*m[p][c]}}return m.map(r=>r.slice(n))}
  function savitzkyGolay(x,w,order){const r=w>>1,c=order+1,ata=Array.from({length:c},()=>Array(c).fill(0));for(let i=0;i<c;i++)for(let j=0;j<c;j++)for(let k=-r;k<=r;k++)ata[i][j]+=k**(i+j);const inv=invert(ata),coef=Array.from({length:w},(_,idx)=>{const k=idx-r;return inv[0].reduce((s,v,p)=>s+v*k**p,0)});return x.map((_,i)=>coef.reduce((s,v,k)=>s+v*x[mirror(i+k-r,x.length)],0))}
  function bilateral(x,w,ss,rs){const r=w>>1;return x.map((v,i)=>{let sum=0,total=0;for(let k=-r;k<=r;k++){const n=x[mirror(i+k,x.length)],d=n-v,weight=Math.exp(-k*k/(2*ss*ss))*Math.exp(-d*d/(2*rs*rs));sum+=n*weight;total+=weight}return sum/total})}
  function smoothFactor(cutoff,rate){const tau=1/(2*Math.PI*cutoff),dt=1/rate;return 1/(1+tau/dt)}
  function oneEuro(x,rate,minCut,beta){if(!x.length)return[];let d=0,y=x[0];return x.map((v,i)=>{if(!i)return y;const rawD=(v-x[i-1])*rate,ad=smoothFactor(1,rate);d+=ad*(rawD-d);const a=smoothFactor(minCut+beta*Math.abs(d),rate);y+=a*(v-y);return y})}
  function kalman(x,r,q){if(!x.length)return[];let estimate=x[0],p=1;return x.map((v,i)=>{if(!i)return estimate;p+=q;const k=p/(p+r);estimate+=k*(v-estimate);p*=1-k;return estimate})}
  function lowPass(x,cutoff,rate){if(!x.length)return[];const dt=1/rate,rc=1/(2*Math.PI*cutoff),a=dt/(rc+dt);let y=x[0];return x.map((v,i)=>i?(y+=a*(v-y)):y)}
  function deadband(x,width){if(!x.length)return[];let last=x[0];return x.map(v=>Math.abs(v-last)>=width?(last=v):last)}
  function slew(x,rate){if(!x.length)return[];let last=x[0];return x.map(v=>(last+=Math.max(-rate,Math.min(rate,v-last))))}
  function strengthFor(def){const raw=+$('strength').value/100;return def.strengthMap?def.strengthMap(raw):raw}
  function apply(key){const def=meta[key],w=+$('window').value,s=strengthFor(def);return def.fn(state.noisy,w,s)}
  function rmse(a,b){return Math.sqrt(a.reduce((s,v,i)=>s+(v-b[i])**2,0)/a.length)}
  function estimateLag(clean,filtered){let bestLag=0,best=Infinity;for(let lag=0;lag<=20;lag++){let e=0,n=0;for(let i=lag;i<clean.length;i++){e+=(clean[i-lag]-filtered[i])**2;n++}e/=n;if(e<best){best=e;bestLag=lag}}return bestLag}
  function updateOutputs(){ $('noiseOut').value=$('noise').value; $('spikesOut').value=$('spikes').value+'%'; $('windowOut').value=$('window').value }
  function update(){
    updateOutputs(); const key=$('filter').value,def=meta[key],filtered=apply(key),compareKey=$('compare').value,compared=compareKey==='none'?null:apply(compareKey);
    const before=rmse(state.clean,state.noisy),after=rmse(state.clean,filtered),reduction=before?100*(1-after/before):0;
    const fixed=state.spikes.filter(i=>Math.abs(filtered[i]-state.clean[i])<Math.abs(state.noisy[i]-state.clean[i])).length;
    $('rmse').textContent=after.toFixed(2); $('reduction').textContent=(reduction>=0?'+':'')+reduction.toFixed(1)+'%'; $('removed').textContent=`${fixed} / ${state.spikes.length}`; $('lag').textContent=estimateLag(state.clean,filtered)+' отсч.';
    $('rmseDelta').textContent=`до фильтра ${before.toFixed(2)}`; $('chartTitle').textContent=compared?`${def.name} vs ${meta[compareKey].name}`:def.name;
    $('explainTitle').textContent=def.name; $('explainText').textContent=description(key); $('formula').textContent=def.formula; $('bestFor').textContent=def.best; $('tradeoff').textContent=def.trade;
    $('strengthName').textContent=def.strength||'Параметр'; const s=strengthFor(def); $('strengthOut').value=s<1?s.toFixed(2):s.toFixed(1); $('strengthLabel').style.opacity=(def.strength||def.strengthMap)?1:.35; $('strength').disabled=!(def.strength||def.strengthMap);
    draw(filtered,compared,key,compareKey);
  }
  function description(key){const descriptions={moving:'Каждая точка заменяется средним соседних измерений. Чем шире окно, тем тише результат — и тем сильнее сглажены быстрые изменения.',ema:'Вес новых данных задаёт α. Фильтр хранит только предыдущее значение, поэтому подходит для потоков и слабых контроллеров.',doubleEma:'Вторая экспонента оценивает и компенсирует задержку первой. Результат быстрее следует за трендом.',median:'Сортирует значения в окне и берёт центральное. Амплитуда одиночного выброса почти не влияет на результат.',hampel:'Оценивает локальную медиану и медианное абсолютное отклонение. Меняет только статистически подозрительные точки.',gaussian:'Соседние точки усредняются с колоколообразными весами: ближайшие влияют сильнее дальних.',savgol:'В каждом окне строится полином методом наименьших квадратов. Это сохраняет кривизну и высоту широких пиков.',bilateral:'Вес зависит и от расстояния, и от похожести значений. Поэтому фильтр сглаживает внутри участка, но старается не пересекать скачок.',oneEuro:'При медленном движении фильтрует сильнее, при быстром автоматически повышает частоту среза и уменьшает лаг.',kalman:'Рекурсивно объединяет предыдущее состояние и измерение с весом, зависящим от их неопределённости.',rc:'Цифровой аналог простой RC-цепи. Пропускает медленные изменения и ослабляет частоты выше среза.',deadband:'Новое значение принимается только после выхода из зоны нечувствительности вокруг предыдущего выхода.',slew:'Ограничивает максимальное изменение за отсчёт, моделируя реальную скорость исполнительного механизма.'};return descriptions[key]}
  function draw(filtered,compared,key,compareKey){
    const canvas=$('chart'),box=canvas.getBoundingClientRect(),dpr=Math.min(devicePixelRatio||1,2);canvas.width=Math.max(1,box.width*dpr);canvas.height=Math.max(1,box.height*dpr);const c=canvas.getContext('2d');c.scale(dpr,dpr);const W=box.width,H=box.height,p={l:44,r:16,t:16,b:32};
    const all=[state.clean,state.noisy,filtered,compared||[]].flat(),min=Math.min(...all),max=Math.max(...all),span=max-min||1,yMin=min-span*.08,yMax=max+span*.08;
    c.clearRect(0,0,W,H);c.font='11px system-ui';c.fillStyle='#76818c';c.strokeStyle='#e5e4df';c.lineWidth=1;
    for(let i=0;i<=5;i++){const y=p.t+(H-p.t-p.b)*i/5;c.beginPath();c.moveTo(p.l,y);c.lineTo(W-p.r,y);c.stroke();const value=yMax-(yMax-yMin)*i/5;c.fillText(value.toFixed(0),3,y+4)}
    const plot=(data,color,width,dash=[])=>{c.save();c.strokeStyle=color;c.lineWidth=width;c.setLineDash(dash);c.beginPath();data.forEach((v,i)=>{const x=p.l+(W-p.l-p.r)*i/(N-1),y=p.t+(H-p.t-p.b)*(yMax-v)/(yMax-yMin);i?c.lineTo(x,y):c.moveTo(x,y)});c.stroke();c.restore()};
    if($('showNoisy').checked)plot(state.noisy,'#b5bbc1',1);if($('showClean').checked)plot(state.clean,'#00a878',2,[7,5]);plot(filtered,'#1268fb',2.6);if(compared)plot(compared,'#7c4dff',2.2);
    const legends=[];if($('showClean').checked)legends.push(['Идеал','#00a878']);if($('showNoisy').checked)legends.push(['Шум','#b5bbc1']);legends.push([meta[key].name,'#1268fb']);if(compared)legends.push([meta[compareKey].name,'#7c4dff']);let lx=p.l;legends.forEach(([label,color])=>{c.fillStyle=color;c.fillRect(lx,H-15,18,3);c.fillStyle='#4e5964';c.fillText(label,lx+24,H-11);lx+=c.measureText(label).width+54});
  }
  function bind(){
    ['filter','compare','window','strength','showClean','showNoisy'].forEach(id=>$(id).addEventListener('input',update));
    ['signal','noise','spikes'].forEach(id=>$(id).addEventListener('input',regenerate));
    $('seed').addEventListener('click',()=>{seed=(seed+7919)%1000000;regenerate()});
    $('reset').addEventListener('click',()=>{seed=42017;$('signal').value='mixed';$('noise').value=18;$('spikes').value=5;$('window').value=7;$('strength').value=20;$('filter').value='hampel';$('compare').value='gaussian';regenerate()});
    new ResizeObserver(()=>update()).observe($('chart').parentElement);
  }
  initSelectors();bind();regenerate();
})();
