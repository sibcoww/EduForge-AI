const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),ts=require('typescript'),Module=require('node:module');const source=ts.transpileModule(fs.readFileSync('src/lib/material-content.ts','utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS}}).outputText;const m=new Module('material-content');m._compile(source,'material-content.js');const {parseMaterial}=m.exports;
test('formats generated headings, lists and fenced code while preserving code characters',()=>{const doc=parseMaterial('# Урок\n- Один\n- Два\n```python\nprint("<script>")\n```');assert.equal(doc.content[0].type,'heading');assert.equal(doc.content[1].content.length,2);assert.equal(doc.content[2].type,'codeBlock');assert.equal(doc.content[2].attrs.language,'python');assert.equal(doc.content[2].content[0].text,'print("<script>")');});test('preserves saved editor JSON and treats HTML as text',()=>{const doc={type:'doc',content:[{type:'paragraph',content:[{type:'text',text:'Урок'}]}]};assert.deepEqual(parseMaterial(JSON.stringify(doc)),doc);assert.equal(parseMaterial('<img onerror=alert(1)>').content[0].content[0].text,'<img onerror=alert(1)>');});

test('material comparison ignores JSON ordering and empty editor defaults but preserves meaningful text and block identity',()=>{
 const {materialFingerprint}=m.exports;
 const server={type:'doc',content:[{type:'heading',attrs:{blockId:'b1',level:2},content:[{type:'text',text:'Урок'}]}]};
 const editor={content:[{content:[{text:'Урок',type:'text'}],attrs:{level:2,blockId:'b1',language:null},type:'heading'}],type:'doc'};
 assert.equal(materialFingerprint(JSON.stringify(server)),materialFingerprint(JSON.stringify(editor)));
 assert.equal(materialFingerprint(''),materialFingerprint('{"type":"doc","content":[{"type":"paragraph","attrs":{"blockId":null}}]}'));
 editor.content[0].content[0].text='Другой урок';assert.notEqual(materialFingerprint(JSON.stringify(server)),materialFingerprint(JSON.stringify(editor)));
});

test('screenshot instructions become red placeholder nodes, not code or fabricated images',()=>{
 const doc=parseMaterial('Шаг 1\n[СКРИНШОТ: Панель с выделенной кнопкой]\n```text\n[СКРИНШОТ: это пример кода]\n```');
 assert.equal(doc.content[1].type,'screenshotPlaceholder');assert.equal(doc.content[1].attrs.description,'Панель с выделенной кнопкой');
 assert.equal(doc.content[2].type,'codeBlock');assert.equal(doc.content[2].content[0].text,'[СКРИНШОТ: это пример кода]');
 assert.deepEqual(parseMaterial(JSON.stringify(doc)),doc);
});

test('renders AI bold markers as marks while preserving inline and fenced code',()=>{
 const doc=parseMaterial('Это **класс**, а это `print("**x**")`.\n```python\nprint("**x**")\na = 2 ** 3\n```');
 assert.equal(doc.content[0].content[1].text,'класс');assert.deepEqual(doc.content[0].content[1].marks,[{type:'bold'}]);assert.equal(doc.content[0].content[3].text,'print("**x**")');assert.deepEqual(doc.content[0].content[3].marks,[{type:'code'}]);assert.equal(doc.content[1].content[0].text,'print("**x**")\na = 2 ** 3');
 const saved=parseMaterial(JSON.stringify({type:'doc',content:[{type:'paragraph',content:[{type:'text',text:'**Объект**'}]}]}));assert.equal(saved.content[0].content[0].text,'Объект');assert.equal(saved.content[0].content[0].marks[0].type,'bold');
});

test('opening an existing image does not create unsaved edits from editor defaults',()=>{
 const old={type:'doc',content:[{type:'image',attrs:{src:'/api/media/id',caption:'Подпись',blockId:'b'}}]};const editor=structuredClone(old);Object.assign(editor.content[0].attrs,{width:'100%',align:'center',originalSrc:'',annotations:[],alt:null,title:null});assert.equal(m.exports.materialFingerprint(JSON.stringify(old)),m.exports.materialFingerprint(JSON.stringify(editor)));editor.content[0].attrs.caption='Другая подпись';assert.notEqual(m.exports.materialFingerprint(JSON.stringify(old)),m.exports.materialFingerprint(JSON.stringify(editor)));
});
