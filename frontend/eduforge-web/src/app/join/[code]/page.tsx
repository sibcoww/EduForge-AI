import StudentWorkspace from "../../../components/StudentWorkspace";
export default async function JoinCodePage({ params }: { params: Promise<{ code: string }> }) {
  const { code } = await params;
  return <StudentWorkspace initialCode={code}/>;
}
