export type Annotation = { id: string; kind: "arrow" | "ellipse" | "rect"; color: string; x1: number; y1: number; x2: number; y2: number; label: string };
export const annotationColors = [{value:"#dc2626",name:"Красный"},{value:"#2563eb",name:"Синий"},{value:"#16a34a",name:"Зелёный"},{value:"#d97706",name:"Оранжевый"},{value:"#9333ea",name:"Фиолетовый"}];
export function annotationLegend(items: Annotation[]) {
 return items.map((a,i)=>`${i+1}. ${annotationColors.find(c=>c.value===a.color)?.name ?? "Цветной"} ${a.kind==="arrow"?"→":a.kind==="ellipse"?"○":"□"}: ${a.label.trim() || "добавьте название выделенного элемента"}`).join("\n");
}
export function drawAnnotations(ctx: CanvasRenderingContext2D, items: Annotation[], width: number, height: number) {
 const scale=Math.max(width,height)/1000;
 items.forEach((a,i)=>{
  const x=a.x1*width,y=a.y1*height,dx=(a.x2-a.x1)*width,dy=(a.y2-a.y1)*height;
  ctx.save();ctx.strokeStyle=a.color;ctx.fillStyle=a.color;ctx.lineWidth=Math.max(2,3*scale);ctx.lineJoin="round";ctx.lineCap="round";
  ctx.beginPath();
  if(a.kind==="rect")ctx.rect(x,y,dx,dy);
  else if(a.kind==="ellipse")ctx.ellipse(x+dx/2,y+dy/2,Math.abs(dx/2),Math.abs(dy/2),0,0,Math.PI*2);
  else {ctx.moveTo(x,y);ctx.lineTo(x+dx,y+dy);const angle=Math.atan2(dy,dx),size=Math.max(10,16*scale);ctx.moveTo(x+dx-size*Math.cos(angle-.45),y+dy-size*Math.sin(angle-.45));ctx.lineTo(x+dx,y+dy);ctx.lineTo(x+dx-size*Math.cos(angle+.45),y+dy-size*Math.sin(angle+.45));}
  ctx.stroke();
  const radius=Math.max(10,13*scale),cx=Math.min(width-radius,Math.max(radius,x)),cy=Math.min(height-radius,Math.max(radius,y));
  ctx.beginPath();ctx.arc(cx,cy,radius,0,Math.PI*2);ctx.fill();ctx.fillStyle="#fff";ctx.font=`bold ${Math.max(12,15*scale)}px sans-serif`;ctx.textAlign="center";ctx.textBaseline="middle";ctx.fillText(String(i+1),cx,cy);ctx.restore();
 });
}
