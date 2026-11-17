const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const ts = require('typescript');
const Module = require('node:module');
const moduleUnderTest = new Module('teaching');
moduleUnderTest._compile(ts.transpileModule(fs.readFileSync('src/lib/teaching.ts', 'utf8'), {compilerOptions:{module:ts.ModuleKind.CommonJS}}).outputText, 'teaching.js');
const {cleanQuiz} = moduleUnderTest.exports;

test('editing and saving a controlled quiz preserves evidence, objective snapshot and review decision', () => {
  const grounding = {revisionId:'revision',objectiveId:'goal',objectiveText:'Применить for',evidence:[{blockId:'block',quote:'for перебирает элементы'}],explanation:'Определение из урока',reviewed:true};
  const quiz = {id:'server-id',title:'Тест',materialId:'material',controlled:true,questions:[{id:'question-id',type:'SingleChoice',text:'Вопрос',points:5,modelAnswer:'Ответ',options:[{id:'option-id',text:'Верно',isCorrect:true},{text:'Неверно',isCorrect:false}],grounding}]};
  const draft = cleanQuiz(quiz);
  assert.deepEqual(draft.questions[0].grounding, grounding);
  assert.equal(draft.controlled,true);
  assert.equal('id' in draft,false);
  assert.equal('id' in draft.questions[0],false);
  assert.equal(JSON.stringify(cleanQuiz(draft)),JSON.stringify(draft));
});

test('cleans emphasis from answers and explanations but preserves code and exact citations',()=>{
 const quiz={title:'**Тест**',materialId:null,questions:[{type:'SingleChoice',text:'Что такое **объект**?',points:1,modelAnswer:'**Экземпляр**',options:[{text:'**Данные**',isCorrect:true},{text:'`2 ** 3`',isCorrect:false}],grounding:{revisionId:'r',objectiveId:'g',objectiveText:'Цель',evidence:[{blockId:'b',quote:'**Точная цитата**'}],explanation:'Это **определение**',reviewed:false}}]};
 const result=cleanQuiz(quiz);assert.equal(result.title,'Тест');assert.equal(result.questions[0].text,'Что такое объект?');assert.equal(result.questions[0].options[1].text,'`2 ** 3`');assert.equal(result.questions[0].grounding.explanation,'Это определение');assert.equal(result.questions[0].grounding.evidence[0].quote,'**Точная цитата**');
});
