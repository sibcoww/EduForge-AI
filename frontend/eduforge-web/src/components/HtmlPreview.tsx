"use client";
export default function HtmlPreview({content}:{content:string}) {
 return <iframe title="Предпросмотр HTML/CSS" sandbox="" srcDoc={`<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; img-src data:">`+content} style={{width:"100%",height:350,background:"white",border:"1px solid #ddd"}}/>;
}
