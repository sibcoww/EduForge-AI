import type { JSONContent } from "@tiptap/react";
// Editor defaults and JSON property order must not mark an unchanged lesson as dirty.
export function materialFingerprint(content: string): string {
 function canonical(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(canonical);
  if (value && typeof value === "object") {
   const fields = Object.entries(value).sort(([a], [b]) => a.localeCompare(b)).flatMap(([key, item]) => {
    if ((key === "marks" || key === "content") && Array.isArray(item) && item.length === 0) return [];
    if (key === "attrs" && item && typeof item === "object") {
     const image=(value as {type?:string}).type==="image";
     const defaults:Record<string,unknown>={caption:"",alt:"",title:"",originalSrc:"",width:"100%",align:"center"};
     const attrs = Object.fromEntries(Object.entries(item).filter(([k,v]) => v!==null&&v!==undefined&&!(image&&(defaults[k]===v||(k==="annotations"&&Array.isArray(v)&&v.length===0)))));
     return Object.keys(attrs).length ? [[key, canonical(attrs)]] : [];
    }
    return [[key, canonical(item)]];
   });
   return Object.fromEntries(fields);
  }
  return value;
 }
 return JSON.stringify(canonical(parseMaterial(content)));
}
function inlineText(value:string):JSONContent[]{
 const result:JSONContent[]=[];const pattern=/(`[^`\r\n]+`|(?<![\w*])\*\*(\S(?:[^\r\n]*?\S)?)\*\*(?![\w*]))/g;let start=0;
 for(const match of Array.from(value.matchAll(pattern))){if(match.index!>start)result.push({type:"text",text:value.slice(start,match.index)});const code=match[0].startsWith("`");result.push({type:"text",text:code?match[0].slice(1,-1):match[2],marks:[{type:code?"code":"bold"}]});start=match.index!+match[0].length;}
 if(start<value.length)result.push({type:"text",text:value.slice(start)});return result;
}
function richText(node:JSONContent):JSONContent{
 if(node.type==="codeBlock")return node;
 return {...node,...(node.content?{content:node.content.flatMap(child=>{if(child.type!=="text"||child.marks?.some(m=>m.type==="code"))return [richText(child)];return inlineText(child.text||"").map(part=>({...part,...((child.marks?.length||part.marks?.length)?{marks:Array.from(new Map([...(child.marks||[]),...(part.marks||[])].map(mark=>[mark.type,mark])).values())}:{})}));})}:{})};
}
export function parseMaterial(content: string): JSONContent {
 try { const value=JSON.parse(content);if(value.type==="doc")return richText(value); }catch{}
 const nodes:JSONContent[]=[];const lines=content.split("\n");const text=inlineText;
 for(let i=0;i<lines.length;i++){
 const line=lines[i];const fence=line.match(/^```([\w+-]*)/);
 if(fence){const code:string[]=[];while(++i<lines.length&&!lines[i].startsWith("```"))code.push(lines[i]);nodes.push({type:"codeBlock",attrs:{language:fence[1]||"plaintext"},content:code.join("\n")?[{type:"text",text:code.join("\n")}]:[]});continue;}
 const screenshot=line.match(/^\s*(?:>\s*)?\[(?:СКРИНШОТ|SCREENSHOT):\s*(.+)\]\s*$/i);if(screenshot){nodes.push({type:"screenshotPlaceholder",attrs:{description:screenshot[1]}});continue;}
 const heading=line.match(/^(#{1,3})\s+(.+)$/);if(heading){nodes.push({type:"heading",attrs:{level:heading[1].length},content:text(heading[2])});continue;}
 const list=line.match(/^\s*[-*]\s+(.+)$/);if(list){const item={type:"listItem",content:[{type:"paragraph",content:text(list[1])}]};const previous=nodes.at(-1);if(previous?.type==="bulletList")previous.content!.push(item);else nodes.push({type:"bulletList",content:[item]});continue;}
 if(line.startsWith("> ")){nodes.push({type:"blockquote",content:[{type:"paragraph",content:text(line.slice(2))}]});continue;}
 nodes.push({type:"paragraph",content:text(line)});
 }
 return {type:"doc",content:nodes.length?nodes:[{type:"paragraph"}]};
}
